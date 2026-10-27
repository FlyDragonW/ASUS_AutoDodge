using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaiwanRider.Editor
{
    // Original visible facade studies aligned to OSM edges; dimensions remain estimated.
    // See Sources/mid-east-2025-shops.json. Facades in the footprint gap use an explicitly estimated line.
    public static class MidEastShopsSetup
    {
        public const string RootName = "Mid-east shops • observed January and May 2025";
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
            m.name=key;m.enableInstancing=true;m.SetColor("_BaseColor",Hex(color));m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
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

        static float width;
        static void Materials()
        {
            M("Wall","#A09B90",0,.16f);M("Floor","#A39D8D",0,.14f);M("Inside","#D0CABC",0,.16f);
            M("Frame","#343B39",.17f,.24f);M("Steel","#959E98",.55f,.34f);M("GlassDark","#314441",.06f,.42f);
            M("Paper","#E9E2C9",0,.18f);M("Ink","#25312B",0,.18f);M("Wood","#AA885C",0,.18f);
            M("WoodDark","#66543F",0,.18f);M("Light","#E9E4C7",0,.23f);M("Red","#A84239",0,.19f);
            M("Yellow","#D7B948",0,.20f);M("BlueGrey","#586C72",.08f,.21f);M("PaleBlue","#73A7B7",0,.2f);
            M("WhiteTile","#DCDDD4",0,.25f);M("Grout","#939B94",0,.15f);M("Magenta","#B2497D",0,.22f);
            M("Shutter","#707C79",.23f,.22f);M("ShutterRib","#515E5B",.28f,.24f);M("Green","#377B45",0,.21f);
            foreach(string key in new[]{"Light","Paper"}){
                var m=materials[key];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(key=="Light"?"#929382":"#666958")*.24f);
            }
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        {B(mat,x,y,z,w,h,.075f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.044f,fontHeight,ink,w-.14f);}
        static void Circle(string mat,float x,float y,float z,float radius)
        {Rod(mat,new Vector3(x,y,z-.011f),new Vector3(x,y,z+.011f),radius,20);}
        static void Shell(float h=3.10f,float depth=2.35f,bool tiled=false)
        {
            // Shallow visible bays only. No new floor count or full building mass is claimed.
            B("Floor",0,.045f,-depth*.45f,width,.09f,depth+.36f);
            B(tiled?"WhiteTile":"Inside",0,h*.5f,-depth,width,h,.09f);
            B("Inside",0,h,-depth*.45f,width,.10f,depth+.36f);
            foreach(float side in new[]{-1f,1f})B(tiled?"WhiteTile":"Wall",side*(width*.5f-.045f),h*.5f,-depth*.49f,.09f,h,depth);
            if(tiled){
                for(float y=.30f;y<h;y+=.30f)B("Grout",0,y,-depth+.048f,width-.12f,.008f,.004f);
                for(float x=-width*.5f+.30f;x<width*.5f-.12f;x+=.30f)B("Grout",x,h*.5f,-depth+.048f,.008f,h-.10f,.004f);
                for(float x=-width*.5f+.45f;x<width*.5f;x+=.45f)B("Grout",x,.093f,-depth*.45f,.006f,.003f,depth+.33f);
                for(float z=-depth;z<.18f;z+=.45f)B("Grout",0,.093f,z,width-.1f,.003f,.006f);
            }
        }
        static void Glass(float x,float w,float h=2.72f,string frame="Frame")
        {
            B("StoreGlass",x,h*.5f+.13f,-.10f,w,h,.021f);
            foreach(float dx in new[]{-w*.5f,w*.5f})B(frame,x+dx,h*.5f+.13f,-.066f,.038f,h+.08f,.065f);
            foreach(float y in new[]{.13f,h+.13f})B(frame,x,y,-.066f,w,.041f,.065f);
        }
        static void UnknownClosureOutsideWindow(float centerX,float windowWidth,float y,float height,float z)
        {
            // Keep the unresolved portion dark without hiding the photographed window sliver.
            float min=-width*.5f+.11f,max=width*.5f-.11f;
            float leftEnd=centerX-windowWidth*.5f-.025f,rightStart=centerX+windowWidth*.5f+.025f;
            if(leftEnd>min)B("GlassDark",(min+leftEnd)*.5f,y,z,leftEnd-min,height,.08f);
            if(max>rightStart)B("GlassDark",(rightStart+max)*.5f,y,z,max-rightStart,height,.08f);
        }
        static void Menu(float x,float y,float z,float w,float h)
        {
            Panel("Paper",x,y,z,w,h,"","#FFFFFF",.1f);
            // Only the column rhythm is recoverable; invent no menu items or prices.
            for(int row=0;row<7;row++)for(int col=0;col<3;col++)B(row%4==0?"Red":"Ink",x-w*.33f+col*w*.31f,y+h*.36f-row*h*.115f,z+.044f,w*.20f,.017f,.006f);
        }
        static void Stool(float x,float y,float z,bool timber=true)
        {
            Rod(timber?"Wood":"Steel",new Vector3(x,y-.028f,z),new Vector3(x,y+.025f,z),.18f,20);
            foreach(float sideX in new[]{-1f,1f})foreach(float sideZ in new[]{-1f,1f}){
                var top=new Vector3(x+sideX*.10f,y-.02f,z+sideZ*.10f);var bottom=new Vector3(x+sideX*.15f,.12f,z+sideZ*.15f);Rod("Frame",bottom,top,.012f,6);
            }
            for(int i=0;i<4;i++){
                float a=i*Mathf.PI*.5f+.7854f,b=a+Mathf.PI*.5f;
                Rod("Frame",new Vector3(x+Mathf.Cos(a)*.17f,.29f,z+Mathf.Sin(a)*.17f),new Vector3(x+Mathf.Cos(b)*.17f,.29f,z+Mathf.Sin(b)*.17f),.009f,6);
            }
        }
        static void Table(float x,float z,float w,float d)
        {
            B("Wood",x,.78f,z,w,.052f,d);
            foreach(float dx in new[]{-1f,1f})foreach(float dz in new[]{-1f,1f})Rod("Frame",new Vector3(x+dx*(w*.5f-.10f),.11f,z+dz*(d*.5f-.08f)),new Vector3(x+dx*(w*.5f-.10f),.75f,z+dz*(d*.5f-.08f)),.017f,6);
        }
        static void VerticalSign(float x,float y,string mat,string text,float h,float depth,float fontHeight)
        {
            B("Frame",x,y,.16f,.10f,h+.08f,.55f);B(mat,x,y,.55f,.17f,h,depth);
            foreach(float side in new[]{-1f,1f})Label(text,new Vector3(x+side*.092f,y,.55f),fontHeight,"#EDE9D9",depth-.08f,side*90f);
        }
        static void MandalayCrop()
        {
            Shell(3.14f,1.45f);
            // January crop resolves a column/239 plate and dark menu board, not the full shop.
            UnknownClosureOutsideWindow(width*.18f,width*.48f,1.45f,2.70f,-.47f);
            Glass(width*.18f,width*.48f,2.62f);B("Wood",width*.18f,.75f,-1.21f,width*.38f,.07f,.36f);
            B("Paper",width*.21f,2.85f,-.35f,width*.48f,.20f,.12f);
            float pole=-width*.5f+.24f;
            B("BlueGrey",pole,1.65f,.12f,.48f,3.30f,.42f);
            foreach(float y in new[]{.31f,1.02f,1.75f,2.50f})B("Frame",pole,y,.337f,.49f,.021f,.018f);
            Panel("Ink",pole,2.80f,.355f,.48f,.79f,"曼德樂","#DDDCCF",.11f);
            Panel("Paper",pole,2.33f,.355f,.38f,.22f,"239","#273A35",.17f);
            Menu(pole,1.58f,.355f,.30f,.59f);
            // Remaining fascia is a neutral closure, deliberately not a fabricated full brand sign.
            B("Wall",0,3.44f,-.13f,width,.52f,.16f);
        }
        static void ColdNoodles()
        {
            Shell(3.13f,1.95f);
            Panel("Yellow",0,3.58f,.08f,width,.84f,"涼麵","#3C3029",.58f);
            B("Ink",0,3.26f,.123f,width*.72f,.023f,.007f);
            Glass(width*.14f,width*.58f,2.69f,"Steel");B("Steel",width*.14f,1.47f,-.057f,.031f,2.70f,.055f);
            B("Steel",width*.14f-.12f,1.35f,-.01f,.018f,.42f,.027f);
            Menu(-width*.365f,1.82f,-.018f,width*.21f,1.65f);
            B("Wood",width*.19f,.80f,-1.46f,width*.40f,.05f,.43f);
            Stool(width*.13f,.47f,-.85f,false);
            float sx=-width*.5f+.14f;
            B("Frame",sx,4.78f,.25f,.10f,1.45f,.70f);B("Yellow",sx,4.78f,.64f,.17f,1.43f,.67f);
            foreach(float side in new[]{-1f,1f})Label("涼\n麵",new Vector3(sx+side*.092f,4.78f,.64f),.43f,"#46372C",.52f,side*90f);
            B("Light",0,3.10f,-.56f,width*.60f,.024f,.035f);
        }
        static void FishBall()
        {
            Shell(3.04f,2.73f,true);
            B("BlueGrey",0,3.05f,.03f,width,.39f,.26f);
            Panel("Paper",0,3.68f,.08f,width,.83f,"佳興魚丸店","#983A37",.43f);
            L("汀州分店",-width*.31f,3.38f,.126f,.13f,"#983A37",.76f);
            foreach(float side in new[]{-1f,1f})B("Steel",side*(width*.5f-.052f),1.49f,.01f,.104f,2.98f,.22f);
            for(int i=0;i<4;i++)B("Light",0,2.88f,-.28f-i*.61f,width*.62f,.025f,.058f);
            // Source474 shows the open steel cooking counter, stacked bowls, wood table and round stools.
            float cx=-width*.235f,cw=width*.49f;
            B("Steel",cx,.70f,-.44f,cw,1.17f,.65f);B("Steel",cx,1.31f,-.42f,cw+.08f,.055f,.80f);
            for(int row=0;row<6;row++)for(int col=0;col<12;col++)Circle("Ink",cx-cw*.44f+col*cw*.88f/11,.30f+row*.10f,-.104f,.029f);
            B("Frame",cx+.16f,1.346f,-.51f,cw*.46f,.024f,.41f);B("Steel",cx+.16f,1.350f,-.51f,cw*.40f,.028f,.34f);
            B("Green",cx-.43f,1.47f,-.47f,.40f,.27f,.35f);B("Paper",cx-.43f,1.62f,-.47f,.36f,.033f,.30f);
            for(int i=0;i<10;i++)Rod("Paper",new Vector3(cx-.55f,1.37f+i*.037f,-.14f),new Vector3(cx-.55f,1.40f+i*.037f,-.14f),.112f,16);
            Menu(cx+.25f,1.75f,-.61f,.67f,.74f);
            Panel("Paper",0,2.52f,-2.670f,width*.83f,.42f,"佳興魚丸店","#9C3834",.26f);
            B("Paper",width*.28f,1.34f,-2.48f,.72f,1.96f,.28f);for(int i=0;i<8;i++)B("Grout",width*.28f,2.04f+i*.027f,-2.329f,.62f,.008f,.012f);
            Table(-width*.21f,.26f,width*.45f,.51f);Stool(-width*.40f,.49f,.75f);Stool(-width*.01f,.49f,.75f);
            Table(width*.34f,-1.22f,.65f,.47f);Stool(width*.31f,.49f,-.63f);
            Menu(width*.34f,2.18f,-1.80f,.72f,1.02f);
            Panel("Green",-width*.5f+.12f,1.92f,.138f,.23f,.16f,"243","#E7E8D4",.095f);
            // Printed people, watermarks, food photos and unseen kitchen details are omitted.
        }
        static void RaphaelCrop()
        {
            Shell(3.07f,1.28f);
            // Most of245 is hidden by the arcade pier. Model its witnessed sliver, not a full imagined jewelry shop.
            UnknownClosureOutsideWindow(width*.28f,width*.32f,1.48f,2.82f,-.40f);
            float win=width*.32f,x=width*.28f;
            Glass(x,win,2.60f,"Steel");B("Wood",x,.82f,-.81f,win*.94f,.83f,.38f);
            B("Light",x,2.76f,-.44f,win*.91f,.043f,.045f);
            for(int i=0;i<5;i++)B("Paper",x-win*.37f+i*win*.18f,1.28f,-.61f,.12f,.031f,.13f);
            B("Wall",-width*.30f,1.62f,.02f,.58f,3.24f,.43f);
            B("Wall",0,3.42f,-.16f,width,.47f,.17f);
            float sx=-width*.5f+.16f;
            B("Frame",sx,4.31f,.16f,.10f,2.58f,.55f);B("Magenta",sx,4.31f,.55f,.17f,2.50f,.73f);
            foreach(float side in new[]{-1f,1f}){
                Label("拉\n非\n爾",new Vector3(sx+side*.092f,4.68f,.55f),.42f,"#EDE9D9",.63f,side*90f);
                Label("珠寶\n銀樓",new Vector3(sx+side*.092f,3.57f,.55f),.17f,"#EDE9D9",.62f,side*90f);
                // January 04:35 shows a fine pale edge and additional tiny copy on the pink blade.
                // Keep the unreadable copy as short geometric marks, not invented wording.
                foreach(float edgeZ in new[]{.203f,.897f})B("Paper",sx+side*.097f,4.31f,edgeZ,.008f,2.44f,.013f);
                foreach(float edgeY in new[]{3.086f,5.534f})B("Paper",sx+side*.097f,edgeY,.55f,.008f,.013f,.707f);
                for(int row=0;row<2;row++)for(int col=0;col<3;col++)B("Paper",sx+side*.098f,3.21f+row*.068f,.37f+col*.18f,.007f,.015f,.097f-(col%2)*.015f);
            }
            Panel("Magenta",width*.24f,2.86f,.02f,width*.43f,.23f,"拉非爾","#F2E2DA",.16f);
        }
        static void Teppanyaki()
        {
            Shell(3.08f,2.32f);
            B("PaleBlue",0,3.62f,.05f,width,.88f,.25f);
            B("Paper",0,3.62f,.184f,width-.10f,.76f,.022f);
            L("喫尤",width*.24f,3.64f,.20f,.63f,"#A53F3A",width*.34f);
            L("平價鐵板燒",-width*.21f,3.58f,.20f,.31f,"#A53F3A",width*.40f);
            // Clear source04:40 lettering supersedes the later OSM typo喫ㄤ.
            B("PaleBlue",0,3.02f,.025f,width,.19f,.30f);
            foreach(float x in new[]{-width*.5f+.105f,width*.5f-.105f,0f})B("PaleBlue",x,1.54f,.015f,.21f,3.07f,.37f);
            Glass(width*.235f,width*.41f,2.60f,"Steel");Glass(-width*.235f,width*.41f,2.60f,"Steel");
            Panel("Paper",0,2.83f,.22f,width-.32f,.30f,"喫尤","#AE4B45",.18f);
            L("120",-width*.35f,2.83f,.266f,.22f,"#AE4B45",.62f);
            Panel("Paper",-width*.5f+.112f,1.88f,.214f,.21f,1.43f,"","#FFFFFF",.1f);
            Label("平\n價\n鐵\n板\n燒",new Vector3(-width*.5f+.112f,1.86f,.260f),.15f,"#A4423C",.18f);
            B("Steel",0,1.03f,-1.71f,width*.65f,.09f,.53f);B("GlassDark",0,.59f,-1.87f,width*.64f,.80f,.28f);
            // Interior is dark/obscured: no invented full seating arrangement.
            // The source shows a long narrow waiting bench outside; preserve its open leg silhouette.
            float benchWidth=width*.62f,bx=width*.12f;
            B("Steel",bx,.49f,.52f,benchWidth,.045f,.31f);
            for(int i=0;i<6;i++){
                float x=bx-benchWidth*.46f+i*benchWidth*.92f/5;
                foreach(float dz in new[]{-.12f,.12f})Rod("Frame",new Vector3(x,.11f,.52f+dz),new Vector3(x,.47f,.52f+dz),.013f,6);
                Rod("Frame",new Vector3(x,.25f,.40f),new Vector3(x,.25f,.64f),.011f,6);
            }
            Panel("Paper",-width*.38f,1.57f,.23f,.62f,1.30f,"","#FFFFFF",.1f);
            for(int i=0;i<3;i++)Circle(i%2==0?"Wood":"Green",-width*.38f,1.18f+i*.30f,.275f,.15f);
        }
        public static Vector3 EstimatedFacadeOutward()
        {
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            Vector3 a=new Vector3(8.6f,0,55.8f);
            Vector3 b=survey.buildings.First(t=>t.id=="773820273").roadFrontEdges.First(e=>e.edgeIndex==0&&e.roadId=="316698035").a;
            var tangent=(b-a).normalized;return new Vector3(tangent.z,0,-tangent.x).normalized;
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build shop studies in Edit mode.");if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("ME25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            if(entries.Length!=5||!new[]{"ME2501","ME2502","ME2503","ME2504","ME2505"}.SequenceEqual(entries.Select(e=>e.id)))throw new InvalidOperationException("Merge the five ME25 reservations before building middle-east shops.");
            if(entries.Any(e=>!string.IsNullOrEmpty(e.buildingId)))throw new InvalidOperationException("The five ME25 groups are explicitly unmapped; do not assign invented footprints.");
            Vector3 normal=EstimatedFacadeOutward();
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build 2025 middle-east shops");
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/MidEastShops/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                switch(entry.id){case "ME2501":MandalayCrop();break;case "ME2502":ColdNoodles();break;case "ME2503":FishBall();break;case "ME2504":RaphaelCrop();break;case "ME2505":Teppanyaki();break;}
                BakeLettering();float top=geometry.Values.SelectMany(g=>g.vertices).Max(v=>v.y);
                if(top>Mathf.Max(entry.height,entry.visualHeight)+.025f)throw new InvalidOperationException(entry.id+" exceeds its visualHeight reservation.");SaveMeshes(go.transform);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);return root;
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
            string path=Art+"/MidEastShopsLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"BlueGrey","PaleBlue","Magenta","Yellow","Grout","Tile","Shutter","ShutterRib","WhiteTile","WoodDark","CaneGreen","CaneDark","WishGreen","ToyBlue","ToyPink","StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/MidEastShops_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="MidEastShops_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Mid-east shop batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=fineSurfaces.Contains(pair.Key);var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
