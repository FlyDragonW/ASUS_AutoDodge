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
    // See Sources/north-east-infill-2025.json; publication dates are separate from capture dates.
    public static class NorthEastInfillSetup
    {
        public const string RootName = "North-east infill • observed January 2025";
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



        static float width;
        static readonly Vector3 FuerGapOutward=new Vector3(-.990669775f,0,.136284250f);
        static void Materials()
        {
            M("Wall","#AAA99F",0,.17f);M("Floor","#9FA397",0,.14f);M("Inside","#BDBEB0",0,.20f);
            M("Frame","#4C5552",.24f,.25f);M("Steel","#A9AEA7",.50f,.38f);
            M("Ink","#252D2D",0,.18f);M("Paper","#DEDCCD",0,.20f);M("Cream","#CAC6AD",0,.19f);
            M("Light","#E7E5D3",0,.25f);M("Wood","#9D947D",0,.17f);
            M("Red","#AF3B37",0,.23f);M("Yellow","#DEC637",0,.24f);M("Green","#3B805F",0,.24f);
            M("Orange","#CC703A",0,.25f);M("DarkBlue","#3E6375",0,.22f);
            M("Cyan","#3B9CA1",0,.22f);M("Blue","#44778F",0,.22f);M("Pink","#B98091",0,.22f);
            M("Shutter","#848381",.17f,.26f);M("ShutterLip","#97968E",.22f,.25f);
            M("Notice","#CEC4BA",0,.15f);M("Dust","#A5A392",0,.13f);
            foreach(string key in new[]{"Light","Yellow","Cyan","Pink"}){
                var m=materials[key];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(key=="Light"?"#AEA995":"#9A977E")*(key=="Light"?.30f:.08f));
            }
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.075f);L(text,x,y,z+.046f,fontHeight,ink,w-.14f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.012f),new Vector3(x,y,z+.012f),r,24); }
        static void Shell(float w,float h=3.12f)
        {
            B("Floor",0,.05f,-1.17f,w,.10f,2.78f);B("Inside",0,h*.5f,-2.52f,w,h,.12f);
            B("Inside",0,h,-1.17f,w,.13f,2.78f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(w*.5f-.055f),h*.5f,-1.18f,.11f,h,2.73f);
            for(int i=0;i<3;i++)B("Light",(i-1)*w*.26f,h-.10f,-1.12f,.045f,.025f,1.85f);
            for(int i=0;i<=Mathf.FloorToInt(w/.50f);i++)B("Steel",-w*.5f+i*.50f,.104f,-1.14f,.008f,.003f,2.65f);
        }
        static void GlassFront(float x,float w,float height=2.72f,bool doubleDoor=true)
        {
            B("StoreGlass",x,height*.5f+.13f,-.11f,w,height,.023f);
            foreach(float dx in new[]{-w*.5f,w*.5f,0f})B("Steel",x+dx,height*.5f+.13f,-.073f,.034f,height+.07f,.06f);
            foreach(float y in new[]{.13f,height+.13f})B("Steel",x,y,-.073f,w,.044f,.06f);
            if(doubleDoor)foreach(float dx in new[]{-.11f,.11f})B("Steel",x+dx,1.23f,.005f,.018f,.36f,.04f);
        }
        static void ProjectingSign(float x,float y,string mat,string text,string ink,float h,float depth,float fontHeight)
        {
            B("Frame",x,y,.20f,.12f,h+.1f,.55f);B(mat,x,y,.59f,.18f,h,depth);
            bool verticalLatin=!text.Contains("\n");
            foreach(float side in new[]{-1f,1f})Label(text,new Vector3(x+side*.099f,y,.59f),fontHeight,ink,verticalLatin?h-.18f:depth-.09f,side*90f,verticalLatin?90:0);
        }


        static void SoftPoster(float x,float y,float z,float w,float h,string baseKey="Paper")
        {
            B(baseKey,x,y,z,w,h,.018f);B("Red",x,y+h*.34f,z+.014f,w*.80f,h*.14f,.007f);
            for(int i=0;i<4;i++)B("Cream",x,y+h*.13f-i*h*.12f,z+.014f,w*.77f,.018f,.007f);
        }
        static void PhonePack(float x,float y,float z,int i,float scale=1f)
        {
            // Original generic phone-accessory packaging seen only as small merchandise shapes.
            B("Paper",x,y,z,.15f*scale,.245f*scale,.025f);
            B(i%4==0?"Cyan":i%4==1?"Ink":i%4==2?"Pink":"Cream",x,y-.01f*scale,z+.018f,.095f*scale,.15f*scale,.011f);
            B("Frame",x,y+.094f*scale,z+.017f,.034f*scale,.014f*scale,.010f);
        }
        static void PhoneRepair()
        {
            Shell(width,3.03f);
            // No guessed shop/company name: only lettering that is readable in January03:25.
            B("Wall",0,3.43f,-.055f,width,.72f,.19f);
            B("Frame",0,3.05f,.03f,width,.15f,.31f);
            B("Paper",0,2.95f,.245f,width,.11f,.58f);
            foreach(float z in new[]{.04f,.43f})B("Light",0,2.886f,z,width-.20f,.025f,.040f);
            float displayW=width-.91f,cx=.33f;
            B("StoreGlass",cx,1.65f,-.10f,displayW,2.34f,.023f);
            foreach(float x in new[]{cx-displayW*.5f,cx,cx+displayW*.5f})B("Steel",x,1.66f,-.068f,.039f,2.39f,.059f);
            foreach(float y in new[]{.47f,2.86f})B("Steel",cx,y,-.068f,displayW+.035f,.049f,.059f);
            B("Wall",cx,.30f,-.015f,displayW,.32f,.14f);
            B("Ink",cx,.127f,.02f,displayW,.048f,.25f);
            GlassFront(-width*.5f+.37f,.60f,2.65f);
            B("Paper",cx,.71f,-.68f,displayW-.14f,.90f,.51f);
            B("Light",cx,1.19f,-.65f,displayW-.05f,.060f,.62f);
            for(int i=0;i<10;i++){
                float x=cx-displayW*.43f+i*displayW*.086f;
                Box(i%3==0?"Cream":i%3==1?"Paper":"DarkBlue",new Vector3(x,1.24f,-.53f),new Vector3(.18f,.035f,.13f),Quaternion.Euler(0,i%2==0?14:-17,0));
            }
            B("Paper",0,1.59f,-2.40f,width-.22f,2.45f,.065f);
            for(int row=0;row<5;row++)for(int col=0;col<12;col++)PhonePack(-width*.42f+col*width*.076f,.70f+row*.43f,-2.35f,row+col);
            float xx=width*.5f-.19f;
            B("Ink",xx,2.16f,.13f,.26f,1.53f,.12f);
            string[] words={"手","機","包","膜"};string[] inks={"#D1DCC8","#C493AB","#65C9BA","#D1B567"};
            for(int i=0;i<4;i++)L(words[i],xx,2.67f-i*.34f,.199f,.25f,inks[i],.22f);
            B("Paper",xx-.31f,2.17f,.095f,.29f,1.55f,.05f);
            L("玻\n璃\n保\n護\n貼",xx-.31f,2.17f,.132f,.20f,"#4D5149",.25f);
            ProjectingSign(width*.5f-.52f,4.18f,"Red","iPhone\n維修","#E9E2D4",1.43f,.79f,.30f);
        }
        static void LotteryA()
        {
            Shell(width,3.03f);
            B("Yellow",0,3.50f,.045f,width,.92f,.19f);
            B("Green",0,3.18f,.15f,width,.26f,.030f);
            // Cropped top fascia copy is unresolved. Preserve its yellow/green fields and simple emblem only.
            Circle("Red",width*.18f,3.57f,.153f,.18f);
            B("Paper",width*.18f,3.59f,.171f,.25f,.10f,.016f);
            B("Yellow",width*.18f,3.46f,.176f,.16f,.025f,.017f);
            B("Paper",-.45f,3.18f,.174f,.84f,.040f,.010f);
            B("Paper",-.45f,3.115f,.174f,.61f,.021f,.010f);
            B("Paper",0,2.985f,.17f,width,.12f,.50f);
            for(int i=0;i<4;i++)B("Light",-width*.36f+i*width*.24f,2.915f,.16f,.12f,.023f,.10f);
            GlassFront(.08f,width-.43f,2.70f);
            foreach(float side in new[]{-1f,1f})B("Yellow",side*(width*.5f-.13f),1.46f,-.012f,.24f,2.83f,.24f);
            B("Cream",.38f,.79f,-1.35f,width*.66f,1.37f,.59f);
            B("Paper",.38f,1.50f,-1.32f,width*.73f,.065f,.66f);
            B("Yellow",0,1.62f,-2.38f,width-.24f,2.61f,.07f);
            SoftPoster(-width*.27f,1.75f,-2.33f,width*.29f,1.50f);SoftPoster(width*.14f,1.97f,-2.33f,width*.27f,1.08f);
            B("Frame",width*.25f,1.80f,-1.27f,.43f,.31f,.12f);
            B("Blue",width*.25f,1.80f,-1.196f,.36f,.25f,.014f);
            SoftPoster(-width*.5f+.36f,1.86f,-.04f,.38f,1.42f,"Yellow");
            // Distinct projecting white sports-lottery lightbox and round yellow/green marker seen before金生.
            float sx=-width*.5f+.13f;
            B("Yellow",sx,3.36f,.62f,.18f,.48f,.95f);
            foreach(float side in new[]{-1f,1f}){
                B("Paper",sx+side*.096f,3.36f,.62f,.014f,.36f,.85f);
                Label("運動彩券",new Vector3(sx+side*.107f,3.36f,.62f),.16f,"#B14F42",.78f,side*90f);
            }
            float rx=width*.5f-.20f;
            Rod("Frame",new Vector3(rx,4.14f,.08f),new Vector3(rx,4.14f,.73f),.024f,8);
            Rod("Yellow",new Vector3(rx-.075f,4.15f,.71f),new Vector3(rx+.075f,4.15f,.71f),.38f,28);
            foreach(float side in new[]{-1f,1f}){
                float x=rx+side*.081f;
                Box("Green",new Vector3(x,3.978f,.71f),new Vector3(.014f,.15f,.54f));
                Box("Cream",new Vector3(x,4.18f,.71f),new Vector3(.014f,.19f,.30f));
                Box("Red",new Vector3(x+side*.009f,4.22f,.71f),new Vector3(.014f,.05f,.21f));
            }
        }
        static void GoldChicken()
        {
            Shell(width,3.12f);
            B("Orange",0,3.52f,.065f,width,.66f,.19f);
            B("Ink",0,3.175f,.155f,width,.060f,.035f);
            L("金 生",0,3.54f,.176f,.49f,"#483329",width-.42f);
            B("Orange",0,3.16f,.18f,width,.15f,.64f);
            foreach(float x in new[]{-width*.35f,-width*.12f,width*.12f,width*.35f}){
                Rod("Light",new Vector3(x,3.073f,.17f),new Vector3(x,3.053f,.17f),.058f,16);
            }
            foreach(float side in new[]{-1f,1f})B("Steel",side*(width*.5f-.095f),1.57f,.033f,.19f,2.98f,.19f);
            B("DarkBlue",width*.5f-.155f,1.53f,.165f,.30f,2.93f,.045f);
            B("Steel",0,3.014f,.036f,width-.33f,.112f,.20f);
            B("Floor",0,.075f,-.65f,width-.34f,.085f,1.64f);
            B("Orange",0,1.58f,-2.40f,width-.21f,3.0f,.075f);
            foreach(float side in new[]{-1f,1f})B("Orange",side*(width*.5f-.14f),1.58f,-1.18f,.06f,2.96f,2.28f);
            // Original thin oval border and stacked wordmark; exact proprietary typography is not traced.
            for(int i=0;i<32;i++){
                float a=i*2*Mathf.PI/32,b=(i+1)*2*Mathf.PI/32;
                Rod("Cream",new Vector3(Mathf.Cos(a)*.43f,1.70f+Mathf.Sin(a)*.61f,-2.345f),new Vector3(Mathf.Cos(b)*.43f,1.70f+Mathf.Sin(b)*.61f,-2.345f),.008f,6);
            }
            L("金\n生",0,1.72f,-2.329f,.39f,"#E2C4A4",.56f);
            B("Ink",-width*.25f,.34f,-.79f,.40f,.075f,.34f);
            Rod("Frame",new Vector3(-width*.25f,.31f,-.79f),new Vector3(-width*.25f,1.67f,-.79f),.019f,8);
            B("Paper",-width*.25f,1.74f,-.80f,.54f,.86f,.032f);
            for(int r=0;r<4;r++)for(int c=0;c<3;c++){
                float x=-width*.25f-.17f+c*.17f,y=1.44f+r*.18f;
                B(c%2==0?"DarkBlue":"Cream",x,y,-.775f,.14f,.14f,.013f);
            }
            Rod("Red",new Vector3(width*.35f,.115f,-2.13f),new Vector3(width*.35f,.43f,-2.13f),.075f,12);
            B("Frame",width*.35f,.46f,-2.13f,.10f,.055f,.065f);
        }
        static void ClosedShell(float h)
        {
            // Closed facades need no invented deep interior. Shallow closure stays clear of neighboring footprints.
            B("Floor",0,.053f,-.12f,width,.10f,.63f);
            B("Inside",0,h*.5f,-.385f,width,h,.11f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.055f),h*.5f,-.14f,.11f,h,.58f);
            B("Wall",0,h-.035f,-.105f,width,.07f,.61f);
        }
        static void AnonymousClosed()
        {
            ClosedShell(3.87f);
            B("Paper",0,1.51f,-.032f,width-.18f,2.84f,.085f);
            foreach(float x in new[]{-width*.29f,width*.23f})B("Dust",x,1.51f,.015f,.014f,2.78f,.007f);
            foreach(float y in new[]{.13f,2.91f})B("Cream",0,y,.017f,width-.18f,.020f,.009f);
            B("Paper",0,3.36f,-.045f,width,.86f,.16f);
            B("Wall",0,2.96f,.065f,width,.080f,.27f);
            B("Dust",width*.36f,1.96f,.017f,.14f,.18f,.028f);
            B("Notice",-width*.37f,1.42f,.017f,.10f,.065f,.012f);
            // No name, no lottery graphics, and no invented莊家班restaurant behind the closed January frontage.
        }
        static void Fuer()
        {
            ClosedShell(3.82f);
            B("Shutter",0,1.49f,-.035f,width-.22f,2.70f,.073f);
            for(int i=0;i<35;i++)B("ShutterLip",0,.18f+i*.075f,.008f,width-.23f,.025f,.021f);
            foreach(float side in new[]{-1f,1f})B("Steel",side*(width*.5f-.14f),1.51f,.015f,.07f,2.77f,.065f);
            B("Frame",0,.15f,.006f,width-.23f,.058f,.047f);
            B("Paper",0,3.39f,-.025f,width,.79f,.12f);
            foreach(float y in new[]{3.14f,3.66f})B("Frame",0,y,.044f,width-.14f,.016f,.012f);
            foreach(float side in new[]{-1f,1f})B("Frame",side*(width*.5f-.072f),3.40f,.044f,.017f,.535f,.012f);
            L("福 二",0,3.40f,.055f,.22f,"#48423D",1.12f);
            B("Paper",0,2.96f,.045f,width,.12f,.40f);
            ProjectingSign(-width*.5f+.17f,4.38f,"Paper","福\n二","#48423D",2.14f,.73f,.34f);
        }
        static void ValidateBounds(string id,float w,float visualHeight)
        {
            float back=id=="NI2504"||id=="NI2505"?-.45f:-2.66f;
            foreach(var pair in geometry)foreach(var p in pair.Value.vertices)
                if(Mathf.Abs(p.x)>w*.5f+.012f||p.y<-.01f||p.y>visualHeight+.012f||p.z<back-.012f||p.z>1.14f)
                    throw new InvalidOperationException(id+" infill geometry exceeds reservation bounds: "+pair.Key+" "+p);
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build north-east infill in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("NI25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            var required=new[]{"NI2501","NI2502","NI2503","NI2504","NI2505"};
            if(entries.Length!=5||!entries.Select(e=>e.id).SequenceEqual(required))throw new InvalidOperationException("Expected five January north-east infill reservations NI2501–NI2505.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January2025 north-east infill");
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;
                Vector3 normal=FuerGapOutward;
                if(!string.IsNullOrEmpty(entry.buildingId))normal=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==entry.edgeIndex&&e.roadId=="316698035").outward;
                else if(entry.id!="NI2505")throw new InvalidOperationException("Only NI2505 uses the documented unmapped gap line.");
                go.transform.localRotation=Quaternion.LookRotation(normal.normalized,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/NorthEastInfill/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                switch(entry.id){case "NI2501":PhoneRepair();break;case "NI2502":LotteryA();break;case "NI2503":GoldChicken();break;case "NI2504":AnonymousClosed();break;case "NI2505":Fuer();break;}
                if(lettering.Count>0)BakeLettering();ValidateBounds(entry.id,entry.width,entry.visualHeight>0?entry.visualHeight:entry.height);SaveMeshes(go.transform);
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
            string path=Art+"/NorthEastInfillLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib","Gold","Purple","Yellow","WarmGlass","PosterBlue","Cream","Yellow","Orange","Green","Cyan","Pink","DarkBlue","Shutter","ShutterLip","Notice","Dust"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/NorthEastInfill_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="NorthEastInfill_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("North-east infill batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
