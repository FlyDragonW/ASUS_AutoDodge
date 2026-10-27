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
    // Three adjoining businesses from September 2025 photographs. No mapped footprint exists here.
    // See Sources/northwest-shops.json. Placement, width, height and unseen backing are explicit estimates.
    public static class NorthWestShopsSetup
    {
        public const string RootName = "Northwest shops • unmapped photo and POI estimate • 166–168";
        public const float EstimatedWidth = 13.23f;
        public const float VisibleHeight = 11.4f;
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
            M("Wall","#B2ACA0",0,.14f);M("Tile","#896F58",0,.24f);M("TileWarm","#8D8170",0,.20f);
            M("Grout","#77776B",0,.12f);M("Frame","#424844",.30f,.25f);M("Steel","#8B9390",.50f,.32f);
            M("GlassDark","#33484A",.10f,.48f);M("Inside","#B3B3A6",0,.15f);M("Floor","#9B998B",0,.15f);
            M("Ink","#343E39",0,.12f);M("Paper","#D0CABA",0,.13f);M("Wood","#665649",0,.18f);
            M("Sign","#BDB79D",0,.17f);M("Blue","#345A6D",0,.21f);M("Red","#A74B37",0,.15f);
            M("Green","#518561",0,.20f);M("Light","#DBDAC2",0,.17f);
            M("Purple","#A04E91",0,.16f);M("Orange","#CC772E",0,.16f);M("Cabinet","#82A58B",0,.18f);M("PosterBlue","#718391",0,.18f);M("Leaf","#536C44",0,.16f);
            // Metrically tiled colour map. This is original geometry-driven material work,
            // not a photograph texture; 1 world unit corresponds to one metre of UV.
            const int n=256;var tex=new Texture2D(n,n,TextureFormat.RGB24,true);var px=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                bool joint=x%26<2||y%13<2;float v=joint?.72f:.94f+Mathf.PerlinNoise(x*.16f,y*.19f)*.045f;
                px[y*n+x]=new Color(v,v*.992f,v*.973f);
            }
            tex.SetPixels(px);tex.Apply();File.WriteAllBytes(Art+"/OriginalSmallTile.png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(Art+"/OriginalSmallTile.png",ImportAssetOptions.ForceSynchronousImport);
            var ti=(TextureImporter)AssetImporter.GetAtPath(Art+"/OriginalSmallTile.png");ti.wrapMode=TextureWrapMode.Repeat;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.SaveAndReimport();
            foreach(string key in new[]{"Tile","TileWarm"}){materials[key].SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/OriginalSmallTile.png"));EditorUtility.SetDirty(materials[key]);}
        }

        const float ThaiCentre=-5.729639f;
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.05f);L(text,x,y,z+.033f,fontHeight,ink,w-.12f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.012f),new Vector3(x,y,z+.012f),r,24); }
        static void FoodGraphic(float x,float y,float z,float radius)
        {
            Circle("Paper",x,y,z,radius);Circle("Wood",x,y,z+.017f,radius*.81f);
            for(int i=0;i<7;i++){
                float a=i*2.399f;Circle(i%3==0?"Leaf":"Light",x+Mathf.Cos(a)*radius*.44f,y+Mathf.Sin(a)*radius*.42f,z+.035f,radius*.17f);
            }
        }
        static void Window(float x,float y,float z,float w,float h,bool bars)
        {
            B("GlassDark",x,y,z,w,h,.04f);
            foreach(float s in new[]{-1f,1f}){
                B("Steel",x+s*w*.5f,y,z+.035f,.055f,h+.08f,.09f);
                B("Steel",x,y+s*h*.5f,z+.035f,w+.08f,.055f,.09f);
            }
            B("Steel",x,y,z+.045f,.043f,h,.07f);
            B("Frame",x,y+h*.20f,z+.045f,w,.038f,.045f);
            if(bars){
                for(int i=0;i<12;i++)B("Steel",x-w*.5f+(i+.5f)*w/12f,y-h*.10f,z+.27f,.025f,h*.79f,.028f);
                B("Steel",x,y-h*.49f,z+.28f,w+.08f,.055f,.30f);
            }
        }
        static void Aircon(float x,float y,float z,float scale=1)
        {
            B("Light",x,y,z,.77f*scale,.55f*scale,.43f*scale);
            Circle("Frame",x,y,z+.222f*scale,.207f*scale);
            for(int i=0;i<5;i++)B("Steel",x,y+(-.15f+i*.075f)*scale,z+.24f*scale,.37f*scale,.018f*scale,.015f);
            B("Red",x+.27f*scale,y+.13f*scale,z+.225f*scale,.09f*scale,.10f*scale,.015f);
        }
        static void PottedTree(float x,float z)
        {
            Rod("Floor",new Vector3(x,.11f,z),new Vector3(x,.52f,z),.25f,16);
            Rod("Wood",new Vector3(x,.40f,z),new Vector3(x,1.88f,z),.033f,8);
            for(int i=0;i<19;i++){
                float a=i*2.399f,yy=1.19f+(i%5)*.16f,rr=.25f+(i%3)*.10f;
                Vector3 p=new Vector3(x+Mathf.Cos(a)*rr,yy,z+Mathf.Sin(a)*rr*.72f);
                Rod("Wood",new Vector3(x,yy-.25f,z),p,.010f,5);
                var q=Quaternion.Euler(12+(i%4)*13,i*53,25);var g=G("Leaf");
                Vector3 A=p+q*new Vector3(-.21f,0,0),C=p+q*new Vector3(.21f,0,0),U=p+q*new Vector3(0,.09f,.014f),D=p+q*new Vector3(0,-.09f,-.014f);
                g.Quad(A,D,C,U);g.Quad(U,C,D,A);
            }
        }
        static void Bench(float x,float z,float width)
        {
            for(int i=0;i<4;i++)B("Wood",x,.48f,z+(i-1.5f)*.09f,width,.042f,.075f);
            for(int i=0;i<3;i++)B("Wood",x,.72f+i*.09f,z-.19f,width,.075f,.045f);
            foreach(float s in new[]{-1f,1f}){
                B("Frame",x+s*(width*.5f-.16f),.28f,z,.045f,.46f,.32f);
                B("Frame",x+s*(width*.5f-.16f),.76f,z-.19f,.04f,.60f,.035f);
            }
        }
        static void OneMillionAndFish()
        {
            // One Million: 5.70 m. Its independent upstairs stair is 2.35 m at photo-left.
            // Opaque sides/back merely close unseen space and are not a claimed building footprint.
            const float left=-2.85f,right=5.20f,mid=1.175f,total=8.05f;
            B("Floor",mid,.085f,-1.52f,total,.17f,3.35f);
            B("Inside",mid,1.45f,-3.13f,total,2.73f,.12f);
            foreach(float x in new[]{left,right})B("Tile",x,5.67f,-1.47f,.13f,11.34f,3.15f);
            B("Tile",mid,7.20f,-3.15f,total,8.2f,.12f);
            B("Tile",mid,9.12f,-.10f,total,4.43f,.24f);
            B("Tile",mid,11.32f,-1.5f,total,.15f,3.2f);
            // Two visible upper window rows; their metric dimensions remain estimated.
            foreach(float y in new[]{8.04f,10.33f}){
                Window(.18f,y,.09f,3.04f,1.41f,true);Window(3.84f,y,.09f,1.02f,1.56f,false);
                B("Steel",mid,y-.89f,.15f,total,.035f,.06f);
            }
            // Bright street sign, dark lower band and separate projecting grille.
            Panel("Paper",0,3.89f,.08f,5.70f,1.26f,"","#303733",.1f);
            L("M",0,4.07f,.121f,.85f,"#777B77",1.40f);
            L("1,000,000",0,3.70f,.123f,.28f,"#424845",1.75f);
            L("花・果茶系專賣",1.92f,4.17f,.124f,.23f,"#333A36",1.59f);
            L("百萬蕃茄料理",-1.88f,4.10f,.124f,.245f,"#555A52",1.68f);
            L("舒肥餐・下午茶",-1.88f,3.74f,.124f,.18f,"#656A60",1.65f);
            Panel("Ink",0,3.19f,.13f,5.70f,.37f,"ONE MILLION TEA · FOOD · BAR","#D8D8CF",.30f);
            for(int i=0;i<4;i++)B("Frame",0,2.92f-i*.115f,.23f,5.65f,.028f,.035f);
            Panel("Frame",0,2.77f,.30f,4.63f,.35f,"ONE MILLION BAR","#DFE1DC",.34f);
            Panel("Paper",0,2.34f,.255f,5.32f,.53f,"韓風輕食     內用 100","#343E3F",.28f);
            // Recessed takeaway bar on photo-left, seating doorway on photo-right.
            B("Inside",1.09f,2.00f,-1.23f,2.96f,1.50f,.06f);
            B("Floor",1.12f,.63f,-1.15f,3.10f,1.14f,.58f);
            B("Paper",1.12f,1.23f,-1.10f,3.14f,.095f,.76f);
            L("1000000 TEA BAR",1.12f,.72f,-.846f,.27f,"#333936",2.85f);
            Panel("Paper",1.15f,2.03f,-1.177f,3.04f,.39f,"1000000","#303733",.38f);
            B("StoreGlass",-1.42f,1.17f,-1.17f,2.02f,2.06f,.045f);
            foreach(float x in new[]{-2.48f,-1.45f,-.39f})B("Frame",x,1.22f,-1.125f,.045f,2.15f,.07f);
            B("Steel",-1.19f,1.13f,-1.071f,.03f,.36f,.04f);
            for(int i=0;i<6;i++)Rod("Paper",new Vector3(1.18f+i*.12f,1.29f,-1.03f),new Vector3(1.18f+i*.12f,1.48f,-1.03f),.046f,10);
            B("Frame",.33f,1.45f,-1.09f,.35f,.34f,.23f);
            Panel("Paper",-.27f,.85f,-.27f,.69f,1.31f,"100\n茶飲 + 輕食","#3A403D",.18f);
            foreach(float s in new[]{-.25f,.25f})B("Frame",-.27f+s,.39f,-.29f,.028f,.66f,.03f);
            Bench(-1.21f,.38f,1.35f);
            // Upstairs entrance: orange jambs, open ascending stair and a closed rear landing.
            B("Orange",4.03f,3.72f,.12f,2.35f,1.43f,.23f);
            L("酸菜魚",4.03f,3.81f,.248f,.36f,"#F1E6D0",1.75f);
            L("188 獨享鍋",4.03f,3.36f,.248f,.20f,"#F1E6D0",1.7f);
            foreach(float x in new[]{2.94f,5.11f})B("Orange",x,1.41f,-.39f,.19f,2.75f,1.14f);
            B("Ink",4.03f,1.47f,-2.90f,2.1f,2.76f,.10f);
            for(int i=0;i<10;i++){
                float top=.20f+i*.185f;
                B("Wood",4.03f,(top+.08f)*.5f,-.09f-i*.285f,1.95f,top-.08f,.29f);
                B("Steel",4.03f,top+.008f,.045f-i*.285f,1.95f,.016f,.035f);
            }
            Rod("Steel",new Vector3(3.08f,.96f,.06f),new Vector3(3.08f,2.60f,-2.51f),.027f);
            Panel("Orange",5.095f,1.65f,.20f,.42f,1.37f,"188\n獨\n享\n鍋","#F4E8CC",.22f);
            // Orange second-floor frame remains visibly distinct from the tea shop below.
            B("Orange",mid,5.69f,.10f,total,2.38f,.19f);
            L("魚你相聚",.95f,6.41f,.212f,.52f,"#EFE6CF",3.60f);
            L("酸菜魚鍋",-1.60f,6.42f,.214f,.25f,"#EFE6CF",1.45f);
            L("酸菜魚鍋\n專賣店",3.73f,5.23f,.212f,.28f,"#F4E8CE",2.26f);
            L("Sauerkraut Fish",3.73f,4.75f,.212f,.14f,"#3D3B32",2.24f);
            L("請\n上\n2\n樓",-2.47f,5.34f,.212f,.245f,"#F3E8CC",.34f);
            Window(-.29f,5.17f,.236f,3.69f,1.36f,false);
            for(int i=0;i<7;i++)B(i%3==0?"PosterBlue":"GlassDark",-1.80f+i*.50f,5.17f,.262f,.48f,1.24f,.025f);
            Aircon(-1.18f,4.73f,.43f,.85f);
            FoodGraphic(4.2f,5.91f,.25f,.37f);
        }
        static void Thai()
        {
            float c=ThaiCentre;
            B("Floor",c,.085f,-1.5f,4.60f,.17f,3.25f);
            B("Inside",c,1.53f,-3.03f,4.60f,2.94f,.12f);
            foreach(float s in new[]{-1f,1f})B("TileWarm",c+s*2.28f,5.16f,-1.38f,.13f,10.31f,3.01f);
            B("TileWarm",c,6.99f,-.11f,4.60f,7.3f,.21f);
            B("TileWarm",c,10.57f,-1.46f,4.60f,.16f,3.25f);
            B("Inside",c,2.60f,-1.29f,4.45f,.12f,2.66f);
            for(int row=0;row<2;row++)for(int col=0;col<3;col++)B("Light",c-1.45f+col*1.43f,2.529f,-.50f-row*1.12f,.11f,.018f,.11f);
            Panel("Purple",c,3.14f,.08f,4.60f,.79f,"泰 正 點  泰式料理","#E8DDCB",.31f);
            L("NICE THAI FOOD",c-.55f,2.91f,.115f,.13f,"#EFE5D2",2.62f);
            for(int i=0;i<11;i++)Box("Paper",new Vector3(c-2.06f+i*.412f,3.45f,.119f),new Vector3(.065f,.065f,.01f),Quaternion.Euler(0,0,45));
            B("Ink",c,2.62f,.13f,3.78f,.38f,.12f);
            // Door back from the outer sign, with a large purple menu panel on photo-left.
            B("StoreGlass",c-.27f,1.38f,-2.07f,2.52f,2.31f,.03f);
            foreach(float x in new[]{-1.56f,-.29f,1f})B("Steel",c+x,1.39f,-2.03f,.046f,2.37f,.065f);
            B("Steel",c-.27f,.21f,-2.01f,2.62f,.07f,.07f);
            L("泰式餐坊",c-.28f,1.13f,-2.047f,.20f,"#DAD9CA",1.85f);
            B("Purple",c+1.57f,1.43f,-2.05f,.87f,2.37f,.09f);
            L("128",c+1.57f,2.05f,-1.997f,.28f,"#E5D4A9",.68f);
            FoodGraphic(c+1.57f,1.51f,-1.985f,.30f);
            FoodGraphic(c+1.57f,.84f,-1.985f,.29f);
            Panel("Purple",c,2.48f,-2.005f,3.90f,.28f,"泰正點  泰式料理","#E8DDCB",.22f);
            PottedTree(c-1.73f,-.55f);PottedTree(c+1.72f,-.66f);Bench(c+.18f,-.13f,1.53f);
            // The same September frame shows a tall food banner and stacked upstairs glazing.
            foreach(float y in new[]{4.82f,7.34f,9.62f})Window(c-.47f,y,.043f,2.44f,1.77f,false);
            Panel("Paper",c-.43f,6.13f,.12f,2.94f,.54f,"Chic Thai 泰式新定食","#62665C",.20f);
            Panel("Paper",c-.43f,8.90f,.12f,2.94f,.49f,"CHILL THAI 泰式小吧","#62665C",.18f);
            B("Paper",c+1.54f,6.74f,.10f,1.08f,5.39f,.20f);
            B("Green",c+1.54f,6.89f,.211f,1.00f,5.07f,.025f);
            FoodGraphic(c+1.54f,4.82f,.24f,.40f);FoodGraphic(c+1.54f,8.14f,.24f,.39f);
            L("168\n元起",c+1.54f,6.84f,.25f,.34f,"#ECEDDB",.90f);
            Panel("Paper",c+1.54f,5.99f,.23f,.79f,1.15f,"BRAND\nNEW\nTHAI\nCUISINE","#495345",.14f);
            // Observed service cabinet and condenser stack between the two businesses.
            float service=-3.135f;
            B("Cabinet",service,.75f,-.04f,.68f,1.34f,.55f);
            B("Frame",service,.77f,.244f,.018f,1.25f,.022f);
            foreach(float s in new[]{-.21f,.21f})B("Steel",service+s,.77f,.252f,.028f,.15f,.025f);
            Aircon(service,2.09f,.09f,.95f);Aircon(service,3.18f,.09f,.95f);
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build photographic shops outside Play mode.");
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var old=parent?parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName)?.gameObject:SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(old)Undo.DestroyObjectImmediate(old);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);
            root.transform.SetPositionAndRotation(new Vector3(-10.616543f,0,-16.996525f),Quaternion.LookRotation(new Vector3(.989348731f,0,-.145564722f),Vector3.up));
            Art="Assets/TaiwanRider/Art/StreetSurvey/NorthWestShops";Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();OneMillionAndFish();Thai();BakeLettering();SaveMeshes(root.transform);
            Undo.RegisterCreatedObjectUndo(root,"Build photo and POI estimated shops");
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
            string path=Art+"/SiteLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"StoreGlass","Purple","Orange","Cabinet","Leaf","PosterBlue","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                var g=pair.Value;string path=Art+"/Survey_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="Survey_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Northwest photo batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
