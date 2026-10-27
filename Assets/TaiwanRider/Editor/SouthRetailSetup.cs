using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TaiwanRider.Editor
{
    // Original visible facade studies aligned to OSM edges; dimensions remain estimated.
    // See Sources/south-retail-2025-shops.json; publication dates are separate from capture dates.
    public static class SouthRetailSetup
    {
        public const string RootName = "South retail • observed 2025 publication imagery";
        static string Art;
        const string FontPath = "Assets/TaiwanRider/Art/Fonts/NotoSansTC.otf";
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Geometry> geometry = new Dictionary<string, Geometry>();
        static readonly List<Lettering> lettering = new List<Lettering>();
        static Font font;

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> indices = new List<int>();
            public readonly List<Color32> colors = new List<Color32>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int n = vertices.Count; Vector3 normal = Vector3.Cross(b-a,c-a).normalized;
                foreach (Vector3 p in new[] { a,b,c,d })
                {
                    vertices.Add(p);
                    uv.Add(Mathf.Abs(normal.y)>.7f ? new Vector2(p.x,p.z) : Mathf.Abs(normal.x)>.7f ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y));
                }
                indices.AddRange(new[] { n,n+1,n+2,n,n+2,n+3 });
            }
        }
        sealed class Lettering
        {
            public string text; public Vector3 position; public float height, width, yaw, roll; public Color color;
        }
        static Color Hex(string code) { ColorUtility.TryParseHtmlString(code,out var c); return c; }
        static Geometry G(string key)
        { if(!geometry.TryGetValue(key,out var value)) geometry[key]=value=new Geometry(); return value; }
        static Material M(string key,string color,float metallic=0f,float smoothness=.24f)
        {
            string path=Art+"/"+key+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) { m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path); }
            m.name=key;m.SetColor("_BaseColor",Hex(color));m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
            materials[key]=m;EditorUtility.SetDirty(m);return m;
        }
        static void Box(string key,Vector3 p,Vector3 size,Quaternion? orientation=null)
        {
            var q=orientation??Quaternion.identity;var g=G(key);Vector3 h=size*.5f;
            Vector3 P(float x,float y,float z)=>p+q*Vector3.Scale(new Vector3(x,y,z),h);
            g.Quad(P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),P(1,-1,-1));
            g.Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1));
            g.Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));
            g.Quad(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));
            g.Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1));
            g.Quad(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1));
        }
        static void Rod(string key,Vector3 a,Vector3 b,float radius,int sides=8)
        {
            var g=G(key);var q=Quaternion.FromToRotation(Vector3.up,b-a);
            for(int i=0;i<sides;i++)
            {
                float t=i*2*Mathf.PI/sides,t1=(i+1)*2*Mathf.PI/sides;
                Vector3 d=q*new Vector3(Mathf.Cos(t)*radius,0,Mathf.Sin(t)*radius),e=q*new Vector3(Mathf.Cos(t1)*radius,0,Mathf.Sin(t1)*radius);
                g.Quad(a+d,b+d,b+e,a+e);g.Quad(b,b+e,b+d,b);g.Quad(a,a+d,a+e,a);
            }
        }
        static void Label(string text,Vector3 p,float height,string color,float width=0f,float yaw=0f,float roll=0f)
        { lettering.Add(new Lettering{text=text,position=p,height=height,width=width,yaw=yaw,roll=roll,color=Hex(color)}); }
        [Serializable] sealed class Survey { public Building[] buildings; }
        [Serializable] sealed class Building { public string id; public Edge[] roadFrontEdges; }
        [Serializable] sealed class Edge { public string roadId; public int edgeIndex; public Vector3 a,b,outward; public float lengthMetres; }
        static void B(string key,float x,float y,float z,float w,float h,float d) => Box(key,new Vector3(x,y,z),new Vector3(w,h,d));
        static void L(string text,float x,float y,float z,float height,string color,float width) => Label(text,new Vector3(x,y,z),height,color,width);
        static void StoreGlass()
        {
            var glass=M("StoreGlass","#AEC1B7",.08f,.72f);
            var color=glass.GetColor("_BaseColor");color.a=.22f;glass.SetColor("_BaseColor",color);
            glass.SetFloat("_Surface",1);glass.SetFloat("_Blend",0);glass.SetFloat("_BlendModePreserveSpecular",1);
            glass.SetFloat("_SrcBlend",(float)BlendMode.One);glass.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);glass.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite",0);glass.SetOverrideTag("RenderType","Transparent");glass.renderQueue=(int)RenderQueue.Transparent;
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.SetShaderPassEnabled("DepthOnly",false);glass.SetShaderPassEnabled("ShadowCaster",false);
            EditorUtility.SetDirty(glass);
        }

        const string ReservationsPath="Assets/TaiwanRider/Data/StorefrontReservations.json";
        [Serializable] sealed class Reservations { public Reservation[] groups; }
        [Serializable] sealed class Reservation
        { public string id,buildingId,sceneName; public int edgeIndex; public Vector3 center; public float width,height,visualHeight; }
        static float width;
        static void Materials()
        {
            M("Wall","#898279",0,.17f);M("Floor","#ACA89C",0,.18f);M("Inside","#D1D1C7",0,.2f);
            M("Frame","#394140",.25f,.3f);M("Steel","#A4ADAA",.52f,.43f);M("GlassDark","#344848",.12f,.5f);
            M("Ink","#22282B",0,.18f);M("Paper","#E7E4D7",0,.22f);M("Wood","#A3957D",0,.2f);
            M("Orange","#E67929",0,.26f);M("Magenta","#CB2386",0,.25f);M("Blue","#4B7C92",0,.21f);
            M("Red","#AF4D4A",0,.2f);M("Light","#EEEBD8",0,.25f);M("Green","#779584",0,.21f);
            M("BladeLime","#88CD40",0,.19f);M("ColumnGrey","#A5AAA5",.05f,.22f);
            materials["Light"].EnableKeyword("_EMISSION");materials["Light"].SetColor("_EmissionColor",Hex("#ABA891")*.45f);
            materials["BladeLime"].EnableKeyword("_EMISSION");materials["BladeLime"].SetColor("_EmissionColor",Hex("#88CD40").linear*.28f);
            StoreGlass();
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.075f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.046f,fontHeight,ink,w-.14f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.009f),new Vector3(x,y,z+.009f),r,24); }
        static void Shell(float w,float depth=2.8f,bool openNorth=false)
        {
            // Only a shallow view depth is reconstructed. Product rows and closure walls are generic.
            B("Floor",0,.04f,-depth*.5f+.07f,w,.16f,depth+.2f);
            B("Inside",0,1.55f,-depth,w,3.1f,.10f);B("Inside",0,3.09f,-depth*.5f,w,.12f,depth);
            B("Wall",w*.5f-.055f,1.55f,-depth*.5f,.11f,3.1f,depth);
            if(!openNorth)B("Wall",-w*.5f+.055f,1.55f,-depth*.5f,.11f,3.1f,depth);
            for(int i=0;i<Mathf.Max(3,Mathf.CeilToInt(w/2.2f));i++)
                B("Light",-w*.38f+i*w*.76f/Mathf.Max(2,Mathf.CeilToInt(w/2.2f)-1),3.012f,-depth*.47f,.055f,.025f,depth*.72f);
        }
        static void GlassFront(float x,float w,float height=2.76f,bool handles=false)
        {
            B("StoreGlass",x,height*.5f+.15f,-.08f,w,height,.022f);
            foreach(float dx in new[]{-w*.5f,w*.5f})B("Steel",x+dx,height*.5f+.15f,-.049f,.038f,height+.04f,.057f);
            foreach(float y in new[]{.15f,height+.15f})B("Steel",x,y,-.049f,w,.039f,.057f);
            if(handles)foreach(float dx in new[]{-.10f,.10f})B("Steel",x+dx,1.22f,.00f,.022f,.39f,.04f);
        }
        static void Frame(Action draw,Vector3 origin,Quaternion rotation)
        {
            var counts=geometry.ToDictionary(p=>p.Key,p=>p.Value.vertices.Count);int labelStart=lettering.Count;
            draw();
            foreach(var batch in geometry){int from=counts.TryGetValue(batch.Key,out var n)?n:0;
                for(int i=from;i<batch.Value.vertices.Count;i++)batch.Value.vertices[i]=origin+rotation*batch.Value.vertices[i];}
            float yaw=rotation.eulerAngles.y;
            for(int i=labelStart;i<lettering.Count;i++){lettering[i].position=origin+rotation*lettering[i].position;lettering[i].yaw+=yaw;}
        }
        static Vector2 Bezier(Vector2 a,Vector2 b,Vector2 c,Vector2 d,float t)
        { float u=1-t;return u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d; }
        static void Swoosh(float x,float y,float z,float scale)
        {
            // Original polygonal study of the visible white check-shaped sign, not traced image pixels.
            const int count=20;var g=G("Paper");
            // An outward-facing local +Z facade is seen with local +X at image left.
            // Mirror the polygon X and reverse winding together: tip must point image-right.
            Vector3 Low(float t){var v=Bezier(new Vector2(-1,.2f),new Vector2(-1.55f,-.76f),new Vector2(-.38f,-.30f),new Vector2(1.65f,.55f),t);return new Vector3(x-v.x*scale,y+v.y*scale,z);}
            Vector3 High(float t){var v=Bezier(new Vector2(-1,.2f),new Vector2(-1.03f,-.26f),new Vector2(-.08f,-.08f),new Vector2(1.65f,.55f),t);return new Vector3(x-v.x*scale,y+v.y*scale,z);}
            for(int i=0;i<count;i++){float a=(float)i/count,b=(float)(i+1)/count;g.Quad(High(a),High(b),Low(b),Low(a));}
        }
        static void NikeFascia(float x0,float z0,float x1,float z1,bool letteringPanel)
        {
            Vector3 along=new Vector3(x1-x0,0,z1-z0);float length=along.magnitude;
            Vector3 normal=new Vector3(-along.z,0,along.x).normalized;
            Frame(()=>{
                B("Orange",0,3.65f,-.11f,length,.98f,.22f);
                B("Frame",0,3.125f,-.06f,length,.07f,.18f);
                if(letteringPanel)L("夢幻運動廣場",0,3.67f,.013f,.29f,"#211C1A",length-.40f);
                else Swoosh(.10f,3.57f,.014f,.70f);
            },new Vector3((x0+x1)*.5f,0,(z0+z1)*.5f),Quaternion.LookRotation(normal,Vector3.up));
        }
        static void PartialStreetCart()
        {
            // A small cart is visible beside the entrance at 02:38. Its address/operator
            // are not established; only the visible food-type sign and a partial cart are shown.
            const float x=1.73f;
            B("Steel",x,.66f,.46f,1.18f,1.15f,.60f);
            B("Paper",x,.68f,.773f,1.08f,.95f,.025f);
            B("Steel",x,1.265f,.46f,1.27f,.07f,.67f);
            foreach(float dx in new[]{-.585f,.585f})B("Steel",x+dx,1.86f,.49f,.026f,1.15f,.04f);
            B("Red",x,2.46f,.49f,1.27f,.34f,.45f);
            L("大腸包小腸",x,2.47f,.727f,.19f,"#FFF1A0",1.13f);
            B("Paper",x,2.215f,.48f,1.13f,.025f,.36f);
            for(int row=0;row<2;row++)for(int col=0;col<4;col++){
                float px=x-.40f+col*.265f,py=.42f+row*.36f;
                B(row==0?"Wood":"Red",px,py,.790f,.16f,.12f,.008f);
                B("Ink",px,py-.095f,.795f,.16f,.012f,.009f);
            }
            for(int i=0;i<5;i++)Rod("Wood",new Vector3(x-.44f+i*.20f,1.33f,.38f),new Vector3(x-.44f+i*.20f,1.33f,.59f),.041f,8);
        }
        static void Nike()
        {
            Shell(width);
            // 02:25 and 02:38 show complementary sides: an orange Nike blade at
            // image-right and a green blade at image-left. The 6.4m slice is estimated.
            B("Ink",0,4.96f,-.04f,width,2.22f,.22f);
            B("Ink",0,6.10f,-.04f,width,.13f,.26f);
            foreach(float x in new[]{-width*.5f+.065f,0,width*.5f-.065f})B("Frame",x,4.96f,.081f,.046f,2.2f,.024f);
            NikeFascia(-width*.5f,.10f,-.50f,.30f,false);
            NikeFascia(-.50f,.30f,width*.5f,.06f,true);
            // +local X appears at the viewer's left when viewing the outward-facing front.
            B("Steel",0,3.045f,.05f,width,.09f,.84f);
            for(int i=0;i<5;i++)B("Frame",0,2.991f,-.31f+i*.185f,width,.012f,.012f);
            foreach(float x in new[]{width*.5f-.16f,.49f,-width*.5f+.085f})
                B("ColumnGrey",x,1.47f,-.01f,x>.4f?.29f:.15f,3.02f,.40f);
            for(int row=0;row<9;row++)foreach(float x in new[]{width*.5f-.16f,.49f})
                B("Steel",x,.23f+row*.30f,.197f,.28f,.012f,.008f);
            B("GlassDark",1.75f,1.51f,-.94f,2.13f,2.72f,.05f);
            PartialStreetCart();
            // Actual entrance is broad and open at image-right, with shoe racks to its sides.
            float entranceWidth=width*.5f+.20f,entranceCenter=(.29f-width*.5f+.10f)*.5f;
            Frame(()=>{
                ShoeInterior(entranceWidth,true);
                foreach(float side in new[]{-1f,1f})GlassFront(side*(entranceWidth*.5f-.21f),.30f,2.72f);
                B("GlassDark",0,1.45f,-2.715f,.94f,2.60f,.025f);
                B("Frame",0,2.76f,-2.36f,entranceWidth-.23f,.22f,.12f);
                for(int row=0;row<2;row++)foreach(float side in new[]{-1f,1f})
                    B("Light",side*.80f,2.987f,-.45f-row*1.10f,.14f,.022f,.14f);
            },new Vector3(entranceCenter,0,0),Quaternion.identity);
            // The green blade is on the image-left. Only legible words are reconstructed;
            // its cropped upper portion is not filled with an invented full business name.
            float sx=width*.5f-.20f;
            B("Frame",sx,5.66f,.23f,.12f,2.89f,.50f);
            B("BladeLime",sx,5.70f,.63f,.18f,2.87f,.78f);
            foreach(float side in new[]{-1f,1f})Frame(()=>{
                L("廣\n場",0,6.18f,.012f,.53f,"#101A0A",.64f);
                L("歡迎\n團購",0,4.81f,.012f,.18f,"#101A0A",.64f);
            },new Vector3(sx+side*.101f,0,.63f),Quaternion.Euler(0,side*90,0));
            // The opposite orange Nike blade is independently visible at 02:25.
            // Both boards coexist; the close 02:38 crop does not include this side.
            float nikeBladeX=-width*.5f+.20f;
            B("Frame",nikeBladeX,5.30f,.23f,.12f,2.35f,.50f);
            B("Orange",nikeBladeX,5.33f,.63f,.18f,2.25f,.78f);
            foreach(float side in new[]{-1f,1f})Frame(()=>{
                Swoosh(.025f,6.13f,.012f,.21f);
            },new Vector3(nikeBladeX+side*.101f,0,.63f),Quaternion.Euler(0,side*90,0));
            foreach(float xx in new[]{-width*.29f,width*.29f}){
                B("Frame",xx,6.31f,.08f,.035f,.32f,.045f);B("Light",xx,6.42f,.15f,.24f,.055f,.18f);}
        }
        static void Ring(string key,float x,float y,float z,float radius,float thickness)
        {
            const int count=32;for(int i=0;i<count;i++){
                float a=i*2*Mathf.PI/count,b=(i+1)*2*Mathf.PI/count;
                Rod(key,new Vector3(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,z),new Vector3(x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius,z),thickness,6);}
        }
        static void TreeMark(float x,float y,float z,float scale)
        {
            // Simplified circular tree mark from the visible graphic; no proprietary pixel texture.
            Ring("Paper",x,y,z,.285f*scale,.022f*scale);
            Rod("Paper",new Vector3(x,y-.21f*scale,z),new Vector3(x,y+.18f*scale,z),.019f*scale,6);
            for(int row=0;row<3;row++)foreach(float side in new[]{-1f,1f}){
                float yy=y+(-.11f+row*.10f)*scale;
                Rod("Paper",new Vector3(x,yy,z),new Vector3(x+side*(.15f-row*.025f)*scale,yy+.085f*scale,z),.015f*scale,6);}
        }
        static void PoyaBand(float w,bool main)
        {
            B("Magenta",0,3.69f,.10f,w,1.0f,.26f);B("Paper",0,3.17f,.23f,w,.036f,.024f);
            L("POYA",main?-.52f:.22f,3.72f,.241f,.54f,"#F0EADD",main?3.0f:2.45f);
            TreeMark(main?1.73f:-1.63f,3.72f,.246f,.93f);
        }
        static void Cosmetics(float x,float y,float z,int variation)
        {
            string mat=variation%5==0?"Magenta":variation%5==1?"Blue":variation%5==2?"Green":variation%5==3?"Paper":"Orange";
            if(variation%3==0){Rod(mat,new Vector3(x,y,z),new Vector3(x,y+.17f,z),.041f,8);B("Paper",x,y+.19f,z,.058f,.045f,.058f);}
            else {B(mat,x,y+.12f,z,.09f,.24f,.063f);B("Paper",x,y+.165f,z-.037f,.069f,.035f,.012f);}
        }
        static void PoyaSlab(string key,float depth,float centerY,float thickness)
        {
            // The actual north edge is slightly oblique to the street facade. A rectangular
            // slab would stick through its glazing by about half a metre at the back.
            var g=G(key);float lo=centerY-thickness*.5f,hi=centerY+thickness*.5f;
            Vector3[] top={new Vector3(-width*.5f+.015f,hi,.12f),new Vector3(width*.5f,hi,.12f),
                new Vector3(width*.5f,hi,-depth),new Vector3(-width*.5f+.50f,hi,-depth)};
            Vector3[] bottom=top.Select(v=>new Vector3(v.x,lo,v.z)).ToArray();
            g.Quad(top[0],top[1],top[2],top[3]);g.Quad(bottom[3],bottom[2],bottom[1],bottom[0]);
            for(int i=0;i<4;i++){int j=(i+1)%4;g.Quad(bottom[i],bottom[j],top[j],top[i]);}
        }
        static void PoyaShell(float depth)
        {
            PoyaSlab("Floor",depth,.04f,.16f);PoyaSlab("Inside",depth,3.09f,.12f);
            B("Inside",.25f,1.55f,-depth,width-.50f,3.1f,.10f);
            B("Wall",width*.5f-.055f,1.55f,-depth*.5f,.11f,3.1f,depth);
            for(int i=0;i<7;i++)B("Light",-width*.36f+i*width*.72f/6,3.012f,-depth*.47f,.055f,.025f,depth*.72f);
        }
        static void Poya(Vector3 northCorner,Vector3 returnEnd,Vector3 origin,Vector3 outward)
        {
            const float returnLength=6.2f,depth=6.36f;
            PoyaShell(depth);PoyaBand(width,true);
            float bay=(width-.36f)/5f;
            for(int i=0;i<5;i++)GlassFront((i-2)*(bay+.02f),bay,2.87f,i==2);
            foreach(float x in new[]{-width*.5f+.06f,width*.5f-.06f})B("Magenta",x,1.59f,.035f,.12f,3.18f,.20f);
            Quaternion worldRotation=Quaternion.LookRotation(outward,Vector3.up),inverse=Quaternion.Inverse(worldRotation);
            Vector3 dir=(returnEnd-northCorner).normalized;
            Vector3 sideOut=new Vector3(dir.z,0,-dir.x); // CCW footprint outward on this north return.
            Vector3 centerWorld=northCorner+dir*(returnLength*.5f)+sideOut*.065f;
            Vector3 localCenter=inverse*(centerWorld-origin),localNormal=inverse*sideOut;
            Frame(()=>{
                PoyaBand(returnLength,false);
                float sideBay=(returnLength-.10f)/3;
                for(int i=0;i<3;i++)GlassFront((i-1)*sideBay,sideBay-.025f,2.87f);
                B("Magenta",returnLength*.5f-.035f,1.59f,.035f,.09f,3.18f,.22f);
            },localCenter,Quaternion.LookRotation(localNormal,Vector3.up));
            // Merchandise is generic colour-block packaging, not a claim about exact 2025 shelving.
            foreach(float z in new[]{-2.50f,-4.82f})for(int row=0;row<4;row++){
                float y=.45f+row*.41f;B("Paper",0,y,z,width*.80f,.045f,.45f);
                B("Paper",0,y+.16f,z+.23f,width*.80f,.028f,.018f);
                for(int i=0;i<34;i++)Cosmetics(-width*.38f+i*width*.76f/33,y+.026f,z,row*41+i);}
            B("Paper",width*.30f,.68f,-1.28f,1.8f,1.18f,.70f);B("Frame",width*.30f,1.44f,-1.43f,.40f,.28f,.22f);
            // Close the minor miter at the actual OSM corner without adding an unsupported upper storey.
            var corner=inverse*(northCorner-origin);B("Magenta",corner.x,1.60f,corner.z,.17f,3.2f,.18f);
        }
        static Reservation[] ReadReservations()
        {
            if(!File.Exists(ReservationsPath))throw new FileNotFoundException("Merge south-retail-reservations.json groups before building.",ReservationsPath);
            var parsed=JsonUtility.FromJson<Reservations>(File.ReadAllText(ReservationsPath));
            var entries=(parsed?.groups??Array.Empty<Reservation>()).Where(e=>e.id=="SR2501"||e.id=="SR2502").ToArray();
            if(entries.Length!=2||entries.Select(e=>e.id).Distinct().Count()!=2)throw new InvalidOperationException("Expected SR2501 and SR2502 reservations exactly once.");
            foreach(var e in entries)if(e.width<=0||e.height<=0||!IsFinite(e.center.x)||!IsFinite(e.center.z))throw new InvalidOperationException("Invalid south retail reservation: "+e.id);
            if(!string.IsNullOrEmpty(entries.Single(e=>e.id=="SR2501").buildingId))throw new InvalidOperationException("Nike253 is explicitly unmapped; no invented host footprint.");
            var p=entries.Single(e=>e.id=="SR2502");if(p.buildingId!="366231798"||p.edgeIndex!=4)throw new InvalidOperationException("POYA uses the verified survey edge4 and its adjacent north return.");
            return entries;
        }
        static bool IsFinite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build south retail facade studies in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            var entries=ReadReservations();font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var host=survey.buildings.Single(b=>b.id=="366231798");
            var edge=host.roadFrontEdges.Single(e=>e.edgeIndex==4&&e.roadId=="316698035");
            var points=JsonUtility.FromJson<FootprintSurvey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json")).buildings.Single(b=>b.id=="366231798").points;
            if(points.Length<7)throw new InvalidOperationException("POYA host footprint is incomplete.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build photographed south retail crops");
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;
                Vector3 normal=entry.id=="SR2501"?new Vector3(-.96709f,0,.25443f):edge.outward.normalized;
                go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/SouthRetail/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();width=entry.width;
                if(entry.id=="SR2501")Nike();else Poya(points[5],points[6],entry.center,normal);
                BakeLettering();SaveMeshes(go.transform);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);return root;
        }
        [Serializable] sealed class FootprintSurvey { public FootprintBuilding[] buildings; }
        [Serializable] sealed class FootprintBuilding { public string id;public Vector3[] points; }
        static void Shoe(float x,float y,float z,int variation)
        {
            string upper=variation%4==0?"Red":variation%4==1?"Ink":variation%4==2?"Blue":"Paper";
            B("Paper",x,y,z,.22f,.024f,.09f);B(upper,x-.015f,y+.046f,z-.006f,.16f,.072f,.087f);
            B(upper,x+.087f,y+.025f,z,.073f,.035f,.087f);
            for(int i=0;i<3;i++)B("Steel",x-.024f+i*.024f,y+.086f,z,.008f,.009f,.056f);
        }
        static void ShoeInterior(float w,bool clearAisle=false)
        {
            // Visible rows of footwear; individual products are generic original mesh approximations.
            if(!clearAisle)B("Wood",0,1.45f,-2.43f,w-.28f,2.7f,.06f);
            else foreach(float side in new[]{-1f,1f})B("Wood",side*w*.34f,1.45f,-2.43f,w*.26f,2.7f,.06f);
            for(int row=0;row<5;row++){
                float y=.49f+row*.43f;
                if(!clearAisle)B("Paper",0,y-.045f,-2.27f,w-.35f,.035f,.31f);
                else foreach(float side in new[]{-1f,1f})B("Paper",side*w*.34f,y-.045f,-2.27f,w*.26f,.035f,.31f);
                int count=Mathf.Max(6,Mathf.FloorToInt((w-.6f)/.35f));
                for(int col=0;col<count;col++){
                    float x=-w*.5f+.34f+col*(w-.68f)/(count-1);
                    if(!clearAisle||Mathf.Abs(x)>.67f)Shoe(x,y,-2.25f,row*7+col);
                }
            }
            foreach(float side in new[]{-1f,1f}){
                B("Wood",side*(w*.5f-.21f),1.36f,-1.21f,.10f,2.46f,2.2f);
                for(int row=0;row<4;row++)for(int col=0;col<4;col++)Shoe(side*(w*.5f-.32f),.62f+row*.44f,-.51f-col*.44f,row+col);
            }
            if(!clearAisle){
                B("Paper",0,.59f,-1.37f,w*.34f,1.06f,.69f);
                for(int i=0;i<4;i++)Shoe(-w*.14f+i*w*.093f,1.13f,-1.34f,i);
            }
        }
        static void BakeLettering()
        {
            font.RequestCharactersInTexture(string.Concat(lettering.Select(l=>l.text)),64,FontStyle.Normal);var g=G("Lettering");
            foreach(var item in lettering)
            {
                var lines=item.text.Split('\n');const float lineHeight=73f;float bottom=float.PositiveInfinity,top=float.NegativeInfinity,longest=0;
                for(int line=0;line<lines.Length;line++)
                {
                    float advance=0;foreach(char c in lines[line])if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal)){bottom=Mathf.Min(bottom,ch.minY-line*lineHeight);top=Mathf.Max(top,ch.maxY-line*lineHeight);advance+=ch.advance;}
                    longest=Mathf.Max(longest,advance);
                }
                if(float.IsInfinity(bottom))continue;float scale=item.height/64f;if(item.width>0&&longest*scale>item.width)scale=item.width/longest;
                var rotation=Quaternion.Euler(0,180f+item.yaw,0)*Quaternion.Euler(0,0,item.roll);float middle=(bottom+top)/2;
                for(int line=0;line<lines.Length;line++)
                {
                    float width=0;foreach(char c in lines[line])if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))width+=ch.advance;float pen=-width/2;
                    foreach(char c in lines[line])
                    {
                        if(!font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))continue;
                        if(!char.IsWhiteSpace(c))
                        {
                            Vector3 P(float px,float py)=>item.position+rotation*new Vector3(px*scale,(py-line*lineHeight-middle)*scale,0);
                            int n=g.vertices.Count;g.vertices.Add(P(pen+ch.minX,ch.minY));g.vertices.Add(P(pen+ch.minX,ch.maxY));g.vertices.Add(P(pen+ch.maxX,ch.maxY));g.vertices.Add(P(pen+ch.maxX,ch.minY));
                            // StreetLabelDepth outputs vertex RGB directly, so sRGB swatches
                            // need conversion in a Linear project to avoid grey/washed-out ink.
                            Color ink=QualitySettings.activeColorSpace==ColorSpace.Linear?item.color.linear:item.color;
                            g.uv.AddRange(new[]{ch.uvBottomLeft,ch.uvTopLeft,ch.uvTopRight,ch.uvBottomRight});for(int i=0;i<4;i++)g.colors.Add(ink);g.indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                        }
                        pen+=ch.advance;
                    }
                }
            }
            var atlas=font.material.mainTexture;if(!atlas)throw new InvalidOperationException("Noto font atlas is missing.");
            var rt=RenderTexture.GetTemporary(atlas.width,atlas.height,0,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;var copy=new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,false);
            string path=Art+"/SouthRetailLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/SouthRetail_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="SouthRetail_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("South retail batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=pair.Key!="Wall" && pair.Key!="Inside" && pair.Key!="Floor";var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
