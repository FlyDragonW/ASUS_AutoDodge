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
    // See Sources/east-shops.json; publication dates are separate from capture dates.
    public static class EastShopsSetup
    {
        public const string RootName = "East shops • observed 2025 publication photographs";
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
            public string text; public Vector3 position; public float height, width, yaw; public Color color;
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
        static void Label(string text,Vector3 p,float height,string color,float width=0f,float yaw=0f)
        { lettering.Add(new Lettering{text=text,position=p,height=height,width=width,yaw=yaw,color=Hex(color)}); }
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
        static void Materials()
        {
            M("Wall","#B8B3A7",0,.16f);M("Tile","#A6A18E",0,.20f);M("Frame","#343735",.25f,.24f);
            M("Steel","#8F9691",.45f,.31f);M("Shutter","#B3B6AC",.35f,.26f);M("ShutterRib","#757B75",.4f,.27f);
            M("GlassDark","#40514E",.06f,.42f);M("Inside","#AFAE9D",0,.15f);M("Floor","#928F80",0,.12f);
            M("Ink","#303A34",0,.16f);M("Paper","#D9D3BC",0,.16f);M("Wood","#8C7858",0,.22f);
            M("Green","#20543D",0,.19f);M("Lavender","#B3ADC7",0,.18f);M("Light","#D6D4BF",0,.19f);
            M("WarmWhite","#C9C4AF",0,.18f);M("Red","#A75342",0,.2f);
            M("Pink","#AD859A",0,.16f);M("Yellow","#B7A647",0,.18f);M("AwningOchre","#B2A05E",0,.13f);M("AwningOrange","#AC7650",0,.13f);
            M("HotpotOrange","#D6A744",0,.18f);M("LanternRed","#BA442C",0,.20f);
        }
        static void Plate(float x,float y,float z,string number)
        {
            B("Green",x,y,z,.24f,.18f,.018f);L(number,x,y,z+.013f,.11f,"#E2E4CC",.20f);
        }
        static void NailCrop()
        {
            // Only the closed shutter and cropped lavender fascia are visible in the
            // 2025 publication. No unknown interior, complete logo or graffiti is invented.
            B("Floor",0,.055f,.62f,2.78f,.11f,1.56f);
            B("Wall",0,3.55f,-.075f,2.78f,.28f,.28f);
            B("Shutter",0,1.44f,.008f,2.52f,2.73f,.055f);
            for(int i=0;i<31;i++)B("ShutterRib",0,.145f+i*.085f,.044f,2.51f,.016f,.018f);
            foreach(float x in new[]{-1.32f,1.32f})B("Wall",x,1.76f,.07f,.14f,3.45f,.22f);
            B("Frame",0,2.84f,.035f,2.66f,.08f,.12f);
            B("Lavender",0,3.18f,.061f,2.63f,.58f,.06f);
            L("Beauty",.20f,3.30f,.097f,.21f,"#443D4A",1.65f);
            L("藝術指甲",.05f,3.06f,.099f,.22f,"#443D4A",1.8f);
            Plate(-1.30f,1.94f,.195f,"225");
            // The small round hanging plaque is a separate shallow disc, not flat paint.
            Rod("Paper",new Vector3(-1.39f,2.53f,.42f),new Vector3(-1.34f,2.53f,.42f),.235f,24);
            Label("藝術\n指甲",new Vector3(-1.329f,2.53f,.42f),.088f,"#55465D",.30f,90);
            Rod("Frame",new Vector3(-1.35f,2.77f,.06f),new Vector3(-1.35f,2.77f,.44f),.015f);
        }
        static void Coffee()
        {
            const float width=2.78f;
            B("Floor",0,.055f,.61f,width,.11f,1.55f);
            B("Inside",0,1.40f,-1.68f,width,2.69f,.08f);
            B("Paper",0,2.84f,-.77f,width,.10f,1.75f);
            foreach(float x in new[]{-1.32f,1.32f})B("Wall",x,1.77f,-.02f,.14f,3.45f,.22f);
            // Narrow black sliding-door frames are the observed front; the shallow
            // behind-glass crop is an estimate, not a full surveyed interior plan.
            foreach(float x in new[]{-.88f,0f,.88f}){
                B("StoreGlass",x,1.43f,-.105f,.84f,2.56f,.018f);
                B("Frame",x-.435f,1.43f,-.08f,.033f,2.60f,.043f);
            }
            B("Frame",0,.16f,-.077f,2.62f,.045f,.05f);B("Frame",0,2.74f,-.077f,2.62f,.045f,.05f);
            B("Wood",-.48f,1.01f,-.24f,1.53f,.06f,.38f);
            foreach(float x in new[]{-.90f,-.17f}){
                B("Wood",x,.62f,-.76f,.31f,.045f,.31f);
                foreach(float dx in new[]{-.12f,.12f})foreach(float dz in new[]{-.12f,.12f})B("Frame",x+dx,.32f,-.76f+dz,.024f,.6f,.024f);
            }
            B("Wood",-.92f,1.37f,-.059f,.44f,1.31f,.035f);
            for(int row=0;row<9;row++)B("Ink",-.92f,.81f+row*.138f,-.036f,.30f,.021f,.007f);
            // This sign is perpendicular to the street and spans the arcade depth.
            // Placing it on the shop wall would misread the perspective photograph.
            B("Paper",1.12f,3.14f,1.065f,.13f,.75f,2.27f);
            B("Green",1.192f,3.14f,1.065f,.022f,.65f,2.15f);
            B("Green",1.048f,3.14f,1.065f,.022f,.65f,2.15f);
            foreach(float side in new[]{-1f,1f}){
                float x=1.12f+side*.088f,yaw=side>0?90:-90;
                Label("雪球咖啡",new Vector3(x,3.30f,1.065f),.22f,"#E0DFCD",1.93f,yaw);
                Label("SNOWBALLER CAFE",new Vector3(x,3.09f,1.065f),.102f,"#E0DFCD",1.95f,yaw);
                Label("咖啡 茶 厚蛋吐司",new Vector3(x,2.945f,1.065f),.103f,"#E0DFCD",1.92f,yaw);
            }
            foreach(float z in new[]{.16f,1.96f})Rod("Steel",new Vector3(1.12f,3.51f,z),new Vector3(1.12f,3.70f,z),.013f);
            Plate(1.30f,1.94f,.111f,"227");
            // Original simplified pavement menu board; no unreadable prices invented.
            Box("Wood",new Vector3(-.65f,.66f,1.09f),new Vector3(.60f,1.10f,.075f),Quaternion.Euler(-7,0,0));
            B("Ink",-.65f,.69f,1.172f,.53f,.89f,.018f);L("厚蛋吐司",-.65f,.92f,1.185f,.13f,"#D0C488",.46f);
            L("咖啡・茶",-.65f,.68f,1.185f,.10f,"#D9D9C8",.46f);
            foreach(float x in new[]{-.88f,-.42f})Rod("Wood",new Vector3(x,.08f,1.4f),new Vector3(x,1.20f,.95f),.02f);
        }
        static void XiHeJia()
        {
            // Conservative sign/door/menu crop from Xihejia-17 and Xihejia-2.
            // The complete restaurant interior and building upper floors are not known.
            const float width=2.20f;
            B("Floor",0,.055f,.52f,width,.11f,1.32f);
            B("Pink",1.005f,1.45f,.026f,.19f,2.86f,.15f);
            B("Wall",-1.025f,1.45f,-.045f,.15f,2.86f,.18f);
            B("GlassDark",-.20f,1.43f,-.048f,1.80f,2.67f,.020f);
            foreach(float x in new[]{-.97f,.80f})B("Wood",x,1.46f,.005f,.058f,2.85f,.08f);
            B("Wood",-.085f,2.865f,.007f,1.85f,.065f,.09f);
            B("Paper",.01f,1.62f,.021f,1.29f,1.91f,.019f);
            L("喜禾嘉",.01f,2.36f,.036f,.13f,"#434739",1.10f);
            L("JOYFULL RICE",.01f,2.11f,.037f,.13f,"#41473D",1.12f);
            L("雞肉飯\n雞汁乾麵\n酥烤雞腿",.01f,1.60f,.038f,.12f,"#55594C",1.07f);
            B("Ink",-.19f,.58f,.084f,1.47f,.80f,.045f);
            L("雞肉飯　意麵\n魯肉飯　烤雞飯\n各式小菜",-.19f,.58f,.113f,.12f,"#D6D4C4",1.36f);
            B("Pink",1.009f,1.75f,.110f,.155f,1.44f,.016f);
            L("原\n張\n記\n雞\n肉\n飯",1.01f,1.75f,.122f,.113f,"#DAC264",.14f);
            // Perpendicular suspended arcade board, retaining the observed two colour blocks.
            B("Paper",.98f,3.24f,1.18f,.135f,.72f,2.43f);
            foreach(float side in new[]{-1f,1f}){
                float x=.98f+side*.077f,yaw=side>0?90:-90;
                B("Pink",x,3.24f,.905f,.017f,.63f,1.82f);
                B("Wood",x,3.24f,2.075f,.017f,.63f,.51f);
                Label("喜禾嘉",new Vector3(x+side*.016f,3.23f,.905f),.36f,"#C9B74B",1.59f,yaw);
                Label("雞肉飯",new Vector3(x+side*.016f,3.35f,2.075f),.088f,"#D9D7C4",.43f,yaw);
                Label("料理家",new Vector3(x+side*.017f,3.12f,2.075f),.092f,"#D9D7C4",.43f,yaw);
            }
            // Only the visible orange/ochre fabric canopy is represented; no extra storeys.
            var tilt=Quaternion.Euler(6,0,0);
            for(int i=0;i<8;i++)Box(i%2==0?"AwningOchre":"AwningOrange",new Vector3(-.9625f+i*.275f,3.77f,1.05f),new Vector3(.275f,.035f,2.55f),tilt);
            foreach(float x in new[]{-1.025f,1.025f})Rod("Steel",new Vector3(x,3.66f,.02f),new Vector3(x,3.59f,2.25f),.017f);
            foreach(float z in new[]{.12f,2.23f})Rod("Frame",new Vector3(.98f,3.60f,z),new Vector3(.98f,3.79f,z),.02f);
        }
        static void RedLantern(float x,float y,float z)
        {
            const int rings=10,sides=20;const float scale=.72f;var g=G("LanternRed");
            Vector3 P(float a,float t)=>new Vector3(x+Mathf.Sin(t)*Mathf.Cos(a)*.23f*scale,y+Mathf.Cos(t)*.205f*scale,z+Mathf.Sin(t)*Mathf.Sin(a)*.23f*scale);
            for(int j=0;j<rings;j++)for(int i=0;i<sides;i++){
                float a=i*2*Mathf.PI/sides,b=(i+1)*2*Mathf.PI/sides,t=j*Mathf.PI/rings,u=(j+1)*Mathf.PI/rings;
                g.Quad(P(a,t),P(b,t),P(b,u),P(a,u));
            }
            Rod("Frame",new Vector3(x,y+.195f*scale,z),new Vector3(x,y+.33f*scale,z),.012f*scale);
            Rod("Yellow",new Vector3(x,y-.22f*scale,z),new Vector3(x,y-.20f*scale,z),.048f*scale,12);
            Rod("LanternRed",new Vector3(x,y-.44f*scale,z),new Vector3(x,y-.24f*scale,z),.014f*scale,8);
            // Original simple ribs stand in for unresolved floral printed graphics.
            for(int i=0;i<6;i++){
                float a=i*2*Mathf.PI/6;
                for(int j=1;j<9;j++){
                    float t=j*Mathf.PI/10,u=(j+1)*Mathf.PI/10;
                    Rod("Yellow",P(a,t),P(a,u),.0035f*scale,5);
                }
            }
        }
        static void Rongcheng()
        {
            // This complete narrow frontage is an estimated subdivision of the shared
            //14.04m OSM edge. Its metric width is not a photograph-derived measurement.
            const float width=2.20f;
            B("Floor",0,.055f,.55f,width,.11f,1.37f);
            B("Wall",1.065f,1.40f,.014f,.07f,2.73f,.12f);
            B("Wall",-1.065f,1.40f,.014f,.07f,2.73f,.12f);
            B("StoreGlass",.714f,1.40f,-.050f,.635f,2.63f,.022f);
            foreach(float x in new[]{.379f,1.035f})B("Frame",x,1.40f,-.018f,.031f,2.68f,.065f);
            B("Steel",.521f,1.19f,.055f,.023f,.49f,.032f);
            foreach(float y in new[]{.945f,1.435f})B("Steel",.521f,y,.027f,.023f,.027f,.075f);
            B("Wood",-.338f,.55f,-.021f,1.38f,1.03f,.10f);
            B("StoreGlass",-.338f,1.74f,-.052f,1.38f,1.28f,.022f);
            B("Wood",-.338f,1.08f,.014f,1.38f,.057f,.060f);
            B("Wood",-.338f,2.43f,.014f,1.38f,.085f,.060f);
            // Shallow visible interior inferred from the doorway photograph, not the full restaurant.
            B("Inside",0,1.42f,-1.59f,width,2.78f,.09f);
            B("Floor",0,.05f,-.79f,width,.10f,1.62f);
            B("Inside",0,2.80f,-.80f,width,.08f,1.65f);
            foreach(float x in new[]{-1.075f,1.075f})B("Inside",x,1.44f,-.79f,.05f,2.70f,1.55f);
            B("Wood",-.37f,.72f,-1.22f,1.28f,.08f,.58f);
            B("Steel",-.37f,.38f,-1.22f,.06f,.63f,.06f);
            B("Light",-.45f,2.72f,-.70f,.80f,.025f,.12f);
            B("Frame",0,2.74f,.07f,width,.21f,.22f);
            for(int i=0;i<6;i++)RedLantern(-.91f+i*.364f,2.46f,.31f);
            // Yellow-orange fascia and six lanterns preserve the source's key rhythm.
            B("Paper",0,3.215f,.067f,width,.45f,.25f);
            B("HotpotOrange",0,3.215f,.202f,width-.04f,.40f,.020f);
            L("容城",.71f,3.235f,.217f,.28f,"#34372E",.69f);
            L("麻辣",.145f,3.235f,.218f,.19f,"#9E3C2D",.40f);
            L("mini",-.223f,3.17f,.219f,.13f,"#E1DBC8",.32f);
            L("火鍋",-.748f,3.235f,.218f,.28f,"#34372E",.62f);
            foreach(float x in new[]{-.94f,.94f})Rod("Frame",new Vector3(x,3.44f,.06f),new Vector3(x,3.79f,.06f),.014f);
        }
        static void Group(Transform parent,Survey survey,string code,string id,float centerZ,float width,Action build)
        {
            var building=survey.buildings.First(b=>b.id==id);
            var edge=building.roadFrontEdges.First(e=>e.roadId=="316698035"&&e.edgeIndex==1);
            var direction=(edge.b-edge.a).normalized;
            float u=(centerZ-edge.a.z)/direction.z;
            u=Mathf.Clamp(u,width*.5f+.03f,edge.lengthMetres-width*.5f-.03f);
            var go=new GameObject(code+" • OSM "+id);go.layer=2;go.transform.SetParent(parent,false);
            go.transform.localPosition=edge.a+direction*u+edge.outward*.065f;
            go.transform.localRotation=Quaternion.LookRotation(edge.outward,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/EastShops/"+code;
            Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();build();BakeLettering();SaveMeshes(go.transform);
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build observed east shops outside Play mode.");
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var old=parent?parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName)?.gameObject:SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(old)Undo.DestroyObjectImmediate(old);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);
            Undo.RegisterCreatedObjectUndo(root,"Build photographed east shop crops");
            Group(root.transform,survey,"E01_225_NailCrop","1287937527",76.95f,2.78f,NailCrop);
            Group(root.transform,survey,"E02_227_Snowball","1287937527",74.02f,2.78f,Coffee);
            Group(root.transform,survey,"E03_233_XiHeJiaCrop","1287937528",65.00f,2.20f,XiHeJia);
            Group(root.transform,survey,"E04_231_Rongcheng","1287937528",67.26f,2.20f,Rongcheng);
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
                var rotation=Quaternion.Euler(0,180f+item.yaw,0);float middle=(bottom+top)/2;
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
            string path=Art+"/EastShopLetteringAtlas.png";
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
                var g=pair.Value;string path=Art+"/EastShop_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="EastShop_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("East shop batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
