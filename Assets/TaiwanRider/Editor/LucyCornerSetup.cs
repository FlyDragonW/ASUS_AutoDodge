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
    // See Sources/lucys-corner-2025.json; all facade dimensions are photographic estimates.
    public static class LucyCornerSetup
    {
        public const string RootName = "Lucys corner • January 2025 observed crop";
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
        static void Frame(Action draw,Vector3 origin,Quaternion rotation)
        {
            var counts=geometry.ToDictionary(p=>p.Key,p=>p.Value.vertices.Count);int labelStart=lettering.Count;
            draw();
            foreach(var batch in geometry){int from=counts.TryGetValue(batch.Key,out var n)?n:0;
                for(int i=from;i<batch.Value.vertices.Count;i++)batch.Value.vertices[i]=origin+rotation*batch.Value.vertices[i];}
            float yaw=rotation.eulerAngles.y;
            for(int i=labelStart;i<lettering.Count;i++){lettering[i].position=origin+rotation*lettering[i].position;lettering[i].yaw+=yaw;}
        }

        static void Materials()
        {
            M("Wall","#576060",0,.22f); M("Floor","#807C75",0,.22f); M("Inside","#DAC5A0",0,.25f);
            M("BlueGrey","#49606B",.08f,.27f); M("Column","#514844",.05f,.21f); M("Purple","#514059",0,.25f);
            M("Frame","#292E2E",.25f,.3f); M("Gold","#C8A664",.50f,.34f); M("Wood","#AD8555",0,.24f);
            M("Cream","#E2D4B9",0,.21f); M("Light","#F6D7A0",0,.2f); M("Jewelry","#D6B158",.68f,.49f);
            M("Paper","#E5DFD6",0,.25f); M("PosterDark","#373D49",0,.2f); M("PosterWarm","#B78D7A",0,.2f);
            materials["Light"].EnableKeyword("_EMISSION");materials["Light"].SetColor("_EmissionColor",Hex("#F6D7A0").linear*.52f);
            StoreGlass();
        }
        static void Face(string key,float x0,float x1,float y0,float y1,float z)
        {
            if(x1-x0<.0001f||y1-y0<.0001f)return;
            G(key).Quad(new Vector3(x0,y0,z),new Vector3(x1,y0,z),new Vector3(x1,y1,z),new Vector3(x0,y1,z));
        }
        static void Ring(float x,float y,float z,float radius)
        {
            const int count=20;
            for(int i=0;i<count;i++){
                float a=i*2*Mathf.PI/count,b=(i+1)*2*Mathf.PI/count;
                Rod("Jewelry",new Vector3(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,z),new Vector3(x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius,z),.007f,5);
            }
        }
        static void NecklaceBust(float x,float y,float z,float scale)
        {
            // Original simplified shop merchandise, not a reproduction of a photographed item.
            var g=G("Cream");
            Vector2[] p={new Vector2(-.16f,0),new Vector2(.16f,0),new Vector2(.14f,.12f),new Vector2(.075f,.25f),new Vector2(.065f,.32f),new Vector2(-.065f,.32f),new Vector2(-.075f,.25f),new Vector2(-.14f,.12f)};
            for(int i=1;i<p.Length-1;i++)g.Quad(new Vector3(x+p[0].x*scale,y+p[0].y*scale,z),new Vector3(x+p[i].x*scale,y+p[i].y*scale,z),new Vector3(x+p[i+1].x*scale,y+p[i+1].y*scale,z),new Vector3(x+p[i+1].x*scale,y+p[i+1].y*scale,z));
            for(int i=0;i<14;i++){
                float a=Mathf.PI+i*Mathf.PI/14,b=Mathf.PI+(i+1)*Mathf.PI/14;
                Rod("Jewelry",new Vector3(x+Mathf.Cos(a)*.098f*scale,y+.20f*scale+Mathf.Sin(a)*.12f*scale,z+.013f),new Vector3(x+Mathf.Cos(b)*.098f*scale,y+.20f*scale+Mathf.Sin(b)*.12f*scale,z+.013f),.007f*scale,5);
            }
            B("Jewelry",x,y+.074f*scale,z+.017f,.030f*scale,.042f*scale,.010f);
        }
        static void Display(float paneWidth)
        {
            // A shallow jewelry display is visible through the U aperture. Exact stock is unknown.
            B("Wood",0,.91f,-.26f,paneWidth*.90f,.62f,.38f);
            B("Cream",0,1.24f,-.26f,paneWidth*.94f,.04f,.42f);
            B("Inside",0,1.83f,-.53f,paneWidth*.96f,1.77f,.06f);
            for(int row=0;row<3;row++){
                float y=1.43f+row*.39f;B("Wood",0,y,-.35f,paneWidth*.91f,.028f,.30f);
                int count=Mathf.Max(3,Mathf.FloorToInt(paneWidth/.36f));
                for(int i=0;i<count;i++){
                    float x=-paneWidth*.38f+i*paneWidth*.76f/(count-1);
                    if(row==0)NecklaceBust(x,y+.025f,-.17f,.65f);
                    else { B("Cream",x,y+.12f,-.29f,.19f,.20f,.025f); Ring(x,y+.12f,-.264f,.034f); }
                }
            }
            B("Light",0,2.82f,-.25f,paneWidth*.86f,.022f,.07f);
        }
        static void UWindow(float panelWidth,float apertureWidth)
        {
            const float bottom=.58f,top=2.94f,h=3.53f;
            float r=apertureWidth*.5f,curveTop=bottom+r;
            Face("BlueGrey",-panelWidth*.5f,panelWidth*.5f,.12f,bottom,0);
            Face("BlueGrey",-panelWidth*.5f,panelWidth*.5f,top,h,0);
            const int steps=44;
            for(int i=0;i<steps;i++){
                float y0=bottom+(curveTop-bottom)*i/steps,y1=bottom+(curveTop-bottom)*(i+1)/steps;
                float x0=Mathf.Sqrt(Mathf.Max(0,r*r-(y0-curveTop)*(y0-curveTop)));
                float x1=Mathf.Sqrt(Mathf.Max(0,r*r-(y1-curveTop)*(y1-curveTop)));
                var g=G("BlueGrey");
                g.Quad(new Vector3(-panelWidth*.5f,y0,0),new Vector3(-x0,y0,0),new Vector3(-x1,y1,0),new Vector3(-panelWidth*.5f,y1,0));
                g.Quad(new Vector3(x0,y0,0),new Vector3(panelWidth*.5f,y0,0),new Vector3(panelWidth*.5f,y1,0),new Vector3(x1,y1,0));
                // Glass only spans the opening. Both openings retain a real curved mesh silhouette.
                G("StoreGlass").Quad(new Vector3(-x0,y0,-.034f),new Vector3(x0,y0,-.034f),new Vector3(x1,y1,-.034f),new Vector3(-x1,y1,-.034f));
            }
            Face("BlueGrey",-panelWidth*.5f,-r,curveTop,top,0);Face("BlueGrey",r,panelWidth*.5f,curveTop,top,0);
            Face("StoreGlass",-r,r,curveTop,top,-.034f);
            for(int i=0;i<44;i++){
                float a=Mathf.PI+i*Mathf.PI/44,b=Mathf.PI+(i+1)*Mathf.PI/44;
                Rod("Gold",new Vector3(Mathf.Cos(a)*r,curveTop+Mathf.Sin(a)*r,.011f),new Vector3(Mathf.Cos(b)*r,curveTop+Mathf.Sin(b)*r,.011f),.027f,8);
            }
            foreach(float side in new[]{-1f,1f})Rod("Gold",new Vector3(side*r,curveTop,.011f),new Vector3(side*r,top,.011f),.027f,8);
            B("Gold",0,top,.011f,apertureWidth,.049f,.045f);
            L("Lucy's",0,.35f,.018f,.245f,"#F2F0E5",panelWidth*.67f);
            Display(apertureWidth);
        }
        static void Door(float panelWidth)
        {
            // The 2025 morning image has a shutter; the January evening walk shows the store open.
            const float r=.89f,spring=1.87f,h=3.53f;
            Face("BlueGrey",-panelWidth*.5f,-r,.12f,spring,0);Face("BlueGrey",r,panelWidth*.5f,.12f,spring,0);
            const int steps=40;
            for(int i=0;i<steps;i++){
                float y0=spring+r*i/steps,y1=spring+r*(i+1)/steps;
                float x0=Mathf.Sqrt(Mathf.Max(0,r*r-(y0-spring)*(y0-spring))),x1=Mathf.Sqrt(Mathf.Max(0,r*r-(y1-spring)*(y1-spring)));
                G("BlueGrey").Quad(new Vector3(-panelWidth*.5f,y0,0),new Vector3(-x0,y0,0),new Vector3(-x1,y1,0),new Vector3(-panelWidth*.5f,y1,0));
                G("BlueGrey").Quad(new Vector3(x0,y0,0),new Vector3(panelWidth*.5f,y0,0),new Vector3(panelWidth*.5f,y1,0),new Vector3(x1,y1,0));
                float a=i*Mathf.PI/steps,b=(i+1)*Mathf.PI/steps;
                Rod("Gold",new Vector3(Mathf.Cos(a)*r,spring+Mathf.Sin(a)*r,.004f),new Vector3(Mathf.Cos(b)*r,spring+Mathf.Sin(b)*r,.004f),.018f,8);
            }
            Face("BlueGrey",-panelWidth*.5f,panelWidth*.5f,spring+r,h,0);
            foreach(float side in new[]{-1f,1f})Rod("Gold",new Vector3(side*r,.13f,.004f),new Vector3(side*r,spring,.004f),.018f,8);
            L("Lucy's",0,3.14f,.02f,.315f,"#F2F0E5",panelWidth-.20f);
            B("Frame",0,.105f,-.07f,1.85f,.09f,.21f);
            // A small glass cabinet is recessed beside the open walking gap, not across it.
            B("Wood",.55f,.49f,-.67f,.48f,.72f,.36f);B("StoreGlass",.55f,1.06f,-.49f,.45f,.39f,.024f);
            B("Gold",.55f,1.28f,-.67f,.49f,.025f,.37f);NecklaceBust(.54f,.89f,-.53f,.65f);
        }
        static void OriginalPoster(float x,float z,float yaw)
        {
            Frame(()=>{
                B("Frame",0,2.15f,0,.44f,.81f,.016f);B("PosterDark",0,2.15f,.013f,.405f,.77f,.008f);
                B("Paper",0,2.43f,.020f,.33f,.042f,.006f);B("PosterWarm",-.04f,2.10f,.020f,.21f,.27f,.006f);
                for(int i=0;i<3;i++)B("Paper",0,1.90f-i*.043f,.022f,.31f-i*.042f,.011f,.006f);
            },new Vector3(x,0,z),Quaternion.Euler(0,yaw,0));
        }
        static void CinemaBand(float x0,float z0,float x1,float z1,bool main)
        {
            Vector3 along=new Vector3(x1-x0,0,z1-z0),normal=new Vector3(-along.z,0,along.x).normalized;
            Frame(()=>{
                B("Purple",0,4.80f,-.085f,along.magnitude,1.20f,.17f);
                B("Frame",0,4.22f,.008f,along.magnitude,.035f,.037f);
                B("Frame",0,5.37f,.008f,along.magnitude,.045f,.037f);
                if(main){
                    L("東南亞",1.91f,4.85f,.017f,.42f,"#E5DFE4",1.30f);
                    B("Paper",1.01f,4.83f,.018f,.021f,.66f,.013f);
                    L("SHOWTIME",-.04f,4.92f,.017f,.25f,"#E5DFE4",1.54f);
                    L("CINEMAS",-.04f,4.69f,.017f,.11f,"#E5DFE4",.82f);
                    L("秀泰影城",-1.81f,4.85f,.017f,.46f,"#E5DFE4",1.80f);
                } else L("東南亞",0,4.85f,.017f,.48f,"#E5DFE4",2.55f);
            },new Vector3((x0+x1)*.5f,0,(z0+z1)*.5f),Quaternion.LookRotation(normal,Vector3.up));
        }
        static void Corner()
        {
            // This is a bounded visible corner crop, not a measured tenancy or the whole 263/265 arcade.
            // Local +X is north (screen-left on the outward front); local -X is the south alley.
            B("Floor",0,.055f,-2.075f,6.14f,.11f,4.20f);
            B("Inside",.20f,1.80f,-4.15f,5.73f,3.37f,.07f);
            B("Inside",3.02f,1.80f,-2.11f,.08f,3.37f,4.04f);
            B("Inside",.20f,3.565f,-2.50f,5.74f,.07f,3.26f);
            B("Wall",0,4.04f,-2.06f,6.14f,.12f,4.12f);
            // Lucy sits behind the outer arcade columns, with a chamfered open doorway.
            Frame(()=>UWindow(4.32f,2.82f),new Vector3(.86f,0,-.91f),Quaternion.identity);
            Frame(()=>Door(2.33345f),new Vector3(-2.125f,0,-1.735f),Quaternion.Euler(0,-45f,0));
            Frame(()=>UWindow(1.56f,1.12f),new Vector3(-2.95f,0,-3.34f),Quaternion.Euler(0,-90f,0));
            B("BlueGrey",.86f,3.55f,-.94f,4.32f,.06f,.11f);
            Frame(()=>B("BlueGrey",0,3.55f,-.025f,2.33345f,.06f,.11f),new Vector3(-2.125f,0,-1.735f),Quaternion.Euler(0,-45f,0));
            B("BlueGrey",-2.92f,3.55f,-3.34f,.11f,.06f,1.56f);
            // Generic cabinets/lighting provide only the visible shallow interior.
            B("Wood",.56f,.59f,-3.75f,2.70f,.95f,.47f);B("Cream",.56f,1.09f,-3.75f,2.76f,.04f,.50f);
            for(int i=0;i<5;i++){
                B("Wood",-.50f+i*.53f,.58f,-3.501f,.025f,.89f,.014f);
                NecklaceBust(-.45f+i*.49f,1.12f,-3.55f,.87f);
            }
            for(int row=0;row<3;row++)B("Light",.63f,1.57f+row*.42f,-4.095f,2.60f,.024f,.052f);
            foreach(float x in new[]{-.74f,1.41f}){
                B("Frame",x,3.50f,-1.23f,.07f,.12f,.07f);
                B("Light",x,3.428f,-1.19f,.13f,.025f,.10f);
            }
            Rod("Cream",new Vector3(-.79f,.15f,-2.49f),new Vector3(-.79f,.62f,-2.49f),.29f,24);
            // Outer arcade follows the shared cinema datum; measurements are image estimates.
            // CA2501's south pier is the shared north boundary. Do not duplicate it here:
            // the previous +2.72m Lucy pier stood only about1.15m from that CA pier.
            // Keep the southern corner pier and its poster; side-return geometry is untouched.
            const float cornerPierX=-2.72f;
            B("Column",cornerPierX,2.075f,0,.70f,4.05f,.70f);
            for(int i=0;i<9;i++)B("Frame",cornerPierX,.25f+i*.445f,.354f,.698f,.010f,.008f);
            OriginalPoster(cornerPierX,.374f,0);
            CinemaBand(-3.055f,.055f,3.055f,.055f,true);
            CinemaBand(-3.055f,-4.125f,-3.055f,.055f,false);
        }
        static void ValidateGeometry(float maxWidth,float maxHeight)
        {
            foreach(var batch in geometry)foreach(var p in batch.Value.vertices)
                if(!IsFinite(p.x)||!IsFinite(p.y)||!IsFinite(p.z)||Mathf.Abs(p.x)>maxWidth*.5f+.001f||p.y<-.001f||p.y>maxHeight+.001f)
                    throw new InvalidOperationException("Lucys corner geometry exceeds its reserved envelope: "+batch.Key+" "+p);
        }
        static bool IsFinite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build Lucy corner in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            var manifest=JsonUtility.FromJson<Reservations>(File.ReadAllText(ReservationsPath));
            var entry=manifest.groups.SingleOrDefault(e=>e.id=="LC2501");
            if(entry==null||entry.buildingId!="228711106"||entry.edgeIndex!=0||Mathf.Abs(entry.width-6.2f)>.001f||entry.visualHeight<5.4f)
                throw new InvalidOperationException("Merge Sources/lucys-corner-reservations.json into StorefrontReservations before building LC2501.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var edge=survey.buildings.Single(b=>b.id==entry.buildingId).roadFrontEdges.Single(e=>e.edgeIndex==entry.edgeIndex&&e.roadId=="316698035");
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build photographed Lucy corner");
            var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;
            go.transform.localRotation=Quaternion.LookRotation(edge.outward.normalized,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/LucyCorner/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();width=entry.width;Materials();Corner();BakeLettering();ValidateGeometry(entry.width,entry.visualHeight);SaveMeshes(go.transform);
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
            string path=Art+"/LucyCornerLetteringAtlas.png";
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
                var g=pair.Value;string path=Art+"/LucyCorner_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="LucyCorner_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Lucys corner batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
