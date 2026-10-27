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
    // Visible facade crops, positioned on OSM edges. Widths and heights are estimates.
    // See Sources/west-frontages.json; photographic publication dates are not capture dates.
    public static class SurveyFacadeSetup
    {
        public const string RootName = "Survey facades • dated 2025 references";
        public const float EstimatedWidth = 11.0f;
        public const float VisibleHeight = 6.85f;
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
        static void Materials()
        {
            M("Wall","#B2ACA0",0,.14f);M("Tile","#9C9480",0,.24f);M("TileWarm","#AC9F87",0,.20f);
            M("Grout","#77776B",0,.12f);M("Frame","#424844",.30f,.25f);M("Steel","#8B9390",.50f,.32f);
            M("GlassDark","#33484A",.10f,.48f);M("Inside","#B3B3A6",0,.15f);M("Floor","#9B998B",0,.15f);
            M("Ink","#343E39",0,.12f);M("Paper","#D0CABA",0,.13f);M("Wood","#665649",0,.18f);
            M("Sign","#BDB79D",0,.17f);M("Blue","#345A6D",0,.21f);M("Red","#A74B37",0,.15f);
            M("Green","#518561",0,.20f);M("Light","#DBDAC2",0,.17f);
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
        static void Counter(float x,float z,float width)
        {
            B("Steel",x,.60f,z,width,1.12f,.58f);B("Steel",x,1.18f,z,width+.06f,.07f,.66f);
            B("GlassDark",x,1.38f,z+.22f,width-.12f,.38f,.02f);
            foreach(float s in new[]{-1f,1f})B("Steel",x+s*(width*.5f-.04f),1.39f,z+.25f,.035f,.45f,.035f);
        }
        static void Stool(float x,float z)
        {
            B("Red",x,.44f,z,.29f,.05f,.29f);
            foreach(float a in new[]{-.11f,.11f})foreach(float b in new[]{-.11f,.11f})B("Steel",x+a,.22f,z+b,.023f,.42f,.023f);
        }
        static void Tub(float x,float y,float z,float radius,float height,string key="Steel")
        {
            Rod(key,new Vector3(x,y-height*.5f,z),new Vector3(x,y+height*.5f,z),radius,18);
        }
        static void Menu(float x,float y,float z,float w,float h)
        {
            B("Paper",x,y,z,w,h,.018f);
            for(int i=0;i<5;i++)B(i==0?"Red":"Ink",x,y+h*.32f-i*h*.15f,z+.012f,w*.78f,i==0?.038f:.012f,.006f);
        }
        static void BrothersAndTofu()
        {
            // Local +X is photograph-left. Centre spacing follows the two mapped POIs;
            // shop widths are interpreted from the 2025 photos, not cadastral boundaries.
            const float bw=3.15f,tw=4.28f,bx=2.33f,tx=-1.43f;
            B("Floor",.20f,.065f,-1.46f,8.04f,.13f,3.05f);
            B("Inside",.20f,1.47f,-3.04f,8.04f,2.86f,.10f);
            B("Inside",.20f,2.85f,-1.50f,8.04f,.10f,3.1f);
            foreach(float x in new[]{-3.64f,.76f,3.96f})B("Tile",x,1.79f,-.02f,.19f,3.55f,.23f);
            B("Tile",.2f,3.40f,-.045f,8.04f,1.04f,.23f);
            B("Blue",bx,2.68f,.06f,bw,.31f,.30f);
            B("Sign",bx,3.27f,.11f,bw,.78f,.28f);
            L("兄弟蚵仔麵線",bx,3.28f,.262f,.49f,"#9D3E30",bw-.20f);
            B("Light",bx,2.52f,.22f,bw-.13f,.035f,.055f);
            B("Inside",bx-bw*.5f+.06f,1.43f,-1.48f,.10f,2.77f,2.95f);
            Counter(bx+.36f,-.40f,1.95f);
            B("Red",bx+.96f,.88f,-.065f,.52f,.60f,.012f);
            L("麵線",bx+.96f,1.01f,-.054f,.18f,"#D7C49A",.44f);
            Menu(bx-.16f,1.47f,-.075f,.40f,.56f);
            Tub(bx+.45f,1.23f,-.37f,.24f,.15f);
            for(int i=0;i<3;i++)B("Paper",bx+.54f,1.25f+i*.042f,-.38f,.22f,.028f,.22f);
            B("Steel",bx+1.16f,1.7f,-2.31f,.62f,.69f,.37f);
            for(int i=0;i<3;i++)B("Light",bx,2.74f,-.70f-i*.90f,.66f,.035f,.14f);
            // Tofu is a completely open door, without a fabricated full-width fascia.
            B("Light",tx,2.72f,.13f,1.76f,.055f,.11f);
            foreach(float x in new[]{tx-tw*.5f,tx+tw*.5f})B("Frame",x,1.41f,.025f,.047f,2.66f,.071f);
            B("Sign",.85f,2.63f,.39f,.15f,.77f,.57f);
            Label("龍潭\n豆花",new Vector3(.936f,2.65f,.4f),.23f,"#884E40",.42f,90f);
            B("Green",.68f,1.94f,.123f,.25f,.15f,.020f);L("237",.68f,1.94f,.136f,.094f,"#D9DFCE",.22f);
            B("Green",.68f,1.75f,.123f,.25f,.15f,.020f);L("235",.68f,1.75f,.136f,.094f,"#D9DFCE",.22f);
            Counter(tx-1.18f,-.64f,1.32f);Tub(tx-1.47f,1.25f,-.63f,.25f,.16f);
            for(int i=0;i<4;i++)B("Paper",tx-1.13f,1.23f+i*.045f,-.59f,.25f,.036f,.25f);
            for(int i=0;i<2;i++){
                float z=-1.08f-i*1.36f;B("Wood",tx+.5f,.75f,z,1.15f,.065f,.57f);
                B("Steel",tx+.5f,.42f,z,.060f,.63f,.06f);Stool(tx+.05f,z+.55f);Stool(tx+1.00f,z+.55f);
            }
            for(int i=0;i<3;i++)B("Light",tx,2.77f,-.63f-i*.98f,1.05f,.035f,.12f);
            Menu(tx-1.93f,1.89f,-.17f,.24f,.34f);
            // Electrical conduits are visible in the July 2025 photograph.
            Rod("Blue",new Vector3(-3.56f,3.02f,.118f),new Vector3(3.90f,3.02f,.118f),.017f);
            Rod("Frame",new Vector3(.67f,3.59f,.12f),new Vector3(.67f,.48f,.12f),.013f);
            // Centre the 8.04m photographed crop before allocating neighbouring 231/233.
            // Its original floor/beam were centred at +.20m; shop widths are unchanged.
            foreach(var batch in geometry.Values)
                for(int i=0;i<batch.vertices.Count;i++)batch.vertices[i]-=Vector3.right*.20f;
            foreach(var label in lettering)label.position-=Vector3.right*.20f;
        }
        static void Lantern(float x,float y,float z,bool pale)
        {
            var key=pale?"Paper":"Red";var g=G(key);const int rings=8,sides=16;float rx=.14f,ry=.32f;
            Vector3 P(float a,float t)=>new Vector3(x+Mathf.Sin(t)*Mathf.Cos(a)*rx,y+Mathf.Cos(t)*ry,z+Mathf.Sin(t)*Mathf.Sin(a)*rx);
            for(int j=0;j<rings;j++)for(int i=0;i<sides;i++){float t=j*Mathf.PI/rings,t1=(j+1)*Mathf.PI/rings,a=i*2*Mathf.PI/sides,a1=(i+1)*2*Mathf.PI/sides;g.Quad(P(a,t),P(a1,t),P(a1,t1),P(a,t1));}
            Rod("Frame",new Vector3(x,y+.29f,z),new Vector3(x,y+.50f,z),.018f);
        }
        static void Corner251()
        {
            const float width=13.15f;
            B("Floor",0,.07f,-.78f,width,.14f,2.36f);
            // Shop backs stay within the building. Width subdivisions are photographic estimates.
            B("Inside",0,1.43f,-1.92f,width,2.8f,.12f);
            foreach(float x in new[]{-6.5f,-2.4f,1.3f,3.6f,6.5f})B("Tile",x,1.63f,-.12f,.18f,3.18f,.25f);
            B("Tile",0,3.01f,-.22f,width,.37f,.54f);
            // A partial FamilyMart band is directly visible at the north (photo-left) edge.
            B("Paper",5.1f,2.79f,.015f,2.77f,.62f,.22f);B("Green",5.1f,3.07f,.135f,2.77f,.10f,.017f);B("Blue",5.1f,2.53f,.135f,2.77f,.075f,.017f);
            L("FamilyMart",5.1f,2.82f,.147f,.26f,"#355F7A",2.39f);
            B("GlassDark",5.13f,1.30f,-.032f,2.61f,2.24f,.028f);
            foreach(float x in new[]{3.84f,5.15f,6.40f})B("Steel",x,1.35f,.017f,.04f,2.25f,.05f);
            B("Green",6.12f,4.21f,.41f,.18f,1.67f,.62f);
            Label("全家",new Vector3(6.222f,4.25f,.39f),.26f,"#DADECE",.48f,90f);
            // Two food/drink fronts seen directly below the terrace; identity not inferred.
            Counter(-.53f,-.46f,2.47f);B("Ink",-.53f,.60f,-.152f,2.32f,.61f,.013f);
            L("60",-.05f,1.84f,.012f,.33f,"#D4CBBA",.60f);
            Counter(-4.35f,-.35f,2.78f);B("Paper",-4.35f,.70f,-.04f,2.61f,.48f,.035f);
            for(int j=0;j<3;j++)for(int i=0;i<5;i++)B(i%2==0?"Blue":"Ink",-5.42f+i*.53f,.56f+j*.13f,-.015f,.33f,.019f,.009f);
            L("紅茶",-4.36f,1.71f,.024f,.24f,"#374740",1.01f);
            B("Paper",-2.55f,1.12f,.19f,.70f,1.68f,.30f);L("北\n投\n紅\n茶",-2.55f,1.22f,.353f,.22f,"#494B3E",.42f);
            foreach(float x in new[]{-5.81f,-4.84f,-3.89f,-.96f,.00f,.94f})Lantern(x,2.17f,.33f,x< -2);
            // Weathered fabric awning: a shallow sloped plane with a narrow wavy valance.
            Box("Wood",new Vector3(-2.27f,2.59f,.47f),new Vector3(8.46f,.055f,1.01f),Quaternion.Euler(10,0,0));
            B("Wood",-2.27f,2.48f,.96f,8.46f,.13f,.05f);
            for(int i=0;i<5;i++)Rod("Frame",new Vector3(-6.30f+i*2.0f,2.73f,-.04f),new Vector3(-6.30f+i*2.0f,2.46f,.98f),.021f);
            // The second-floor terrace is long and open, with a dark timber balustrade.
            B("Floor",-2.15f,3.23f,-.04f,8.68f,.19f,1.76f);B("TileWarm",-2.15f,4.47f,-1.02f,8.68f,2.43f,.16f);
            for(int i=0;i<7;i++){
                float x=-5.79f+i*1.21f;B("GlassDark",x,4.39f,-.915f,1.10f,1.72f,.025f);
                foreach(float d in new[]{-.55f,.55f})B("Wood",x+d,4.39f,-.88f,.045f,1.82f,.06f);
                B("Wood",x,4.49f,-.88f,1.10f,.045f,.06f);
            }
            for(int i=0;i<29;i++)B("Wood",-6.36f+i*.301f,3.82f,.73f,.048f,1.02f,.06f);
            B("Wood",-2.14f,4.32f,.74f,8.64f,.11f,.14f);B("Wood",-2.14f,3.42f,.74f,8.64f,.06f,.08f);
            B("Ink",-2.20f,4.04f,.785f,3.20f,.47f,.046f);L("澗面 - Cafe Bar & Bistro",-2.20f,4.05f,.817f,.245f,"#DCC489",3.01f);
            L("不澗不散",-2.00f,5.35f,-.915f,.25f,"#D7C68D",1.78f);
            B("Paper",-2.14f,5.86f,.05f,8.79f,.095f,2.22f);
            for(int i=0;i<9;i++){
                float x=-6.40f+i*1.06f;Rod("Frame",new Vector3(x,5.81f,-1.0f),new Vector3(x,5.79f,1.12f),.028f);
                if(i%2==0){Tub(x,5.46f,-.40f,.11f,.08f,"Light");B("Wood",x,3.54f,.52f,.30f,.28f,.27f);}
            }
            // Upper-wall crop from the same November image. Total building height is unknown.
            B("Tile",0,6.34f,-.91f,width,.65f,.21f);
            for(int i=0;i<8;i++){float x=-5.82f+i*1.66f;B("GlassDark",x,6.98f,-.78f,1.44f,.61f,.027f);B("Frame",x-.74f,6.98f,-.74f,.04f,.68f,.06f);}
            B("Tile",0,7.42f,-.89f,width,.22f,.24f);
            B("Steel",-3.15f,6.51f,-.54f,.72f,.46f,.38f);B("Ink",-3.15f,6.51f,-.339f,.52f,.27f,.02f);
        }
        static void Bookstore()
        {
            // This footprint is a low roof over the basement bookshop entrance, not a tower.
            const float width=21.75f;
            // The visual district ground must omit this same local rectangle:
            // X [-10.16,-6.72], Z [-2.05,.13]. Dimensions are estimated, not surveyed.
            const float gapLeft=-10.16f,gapRight=-6.72f,gapBack=-2.05f,gapFront=.13f;
            const float floorLeft=-width*.5f,floorRight=width*.5f,floorBack=-2.13f;
            B("Floor",(floorLeft+gapLeft)*.5f,.09f,-1f,gapLeft-floorLeft,.18f,2.26f);
            B("Floor",(gapRight+floorRight)*.5f,.09f,-1f,floorRight-gapRight,.18f,2.26f);
            B("Floor",(gapLeft+gapRight)*.5f,.09f,(floorBack+gapBack)*.5f,gapRight-gapLeft,.18f,gapBack-floorBack);
            B("Wood",0,2.83f,-.15f,width,.22f,2.02f);
            B("Wood",0,3.24f,-.52f,width,.10f,2.84f);
            B("Wood",0,3.02f,-1.37f,width,.33f,.10f);
            for(int i=0;i<11;i++){
                float x=-10.53f+i*2.10f;
                // The two entrance bays remain open above the descending stair well.
                if(i!=1)B("Frame",x,1.49f,.60f,.10f,2.50f,.11f);
                if(i>2){B("GlassDark",x-.98f,1.48f,.565f,1.86f,2.35f,.024f);B("Frame",x-.98f,1.02f,.605f,1.86f,.05f,.05f);}
            }
            // Entrance at the north end (photo-left), with two pale vertical name panels.
            foreach(float x in new[]{-10.51f,-6.39f}){
                B("Paper",x,1.5f,.64f,.55f,2.43f,.16f);
                L("金\n石\n堂\n書\n店",x,1.62f,.731f,.275f,"#374640",.38f);
            }
            // Close the stair well below ground rather than masking the descent with
            // the former opaque plane halfway down the stairs. No basement layout is asserted.
            B("Inside",gapLeft-.05f,-.335f,(gapBack+gapFront)*.5f,.10f,1.03f,gapFront-gapBack);
            B("Inside",gapRight+.05f,-.335f,(gapBack+gapFront)*.5f,.10f,1.03f,gapFront-gapBack);
            B("Ink",-8.44f,-.27f,gapBack-.05f,3.44f,.90f,.10f);
            B("Floor",-8.44f,-.61f,-1.925f,3.44f,.12f,.25f);
            B("Floor",-8.44f,.1275f,.10f,3.44f,.105f,.06f);
            for(int i=0;i<6;i++){
                float top=.075f-i*.125f,bottom=-.76f;
                B("Floor",-8.44f,(top+bottom)*.5f,-.09f-i*.31f,3.44f,top-bottom,.32f);
                B("Paper",-7.40f,1.01f-i*.13f,-.18f-i*.30f,.035f,.70f,.09f);
            }
            Rod("Steel",new Vector3(-7.40f,1.31f,.06f),new Vector3(-7.40f,.62f,-1.59f),.035f);
            for(int i=0;i<6;i++){float x=-5.30f+i*2.23f;B("Wood",x,.35f,.58f,1.68f,.55f,.51f);B("Green",x,.73f,.52f,1.46f,.29f,.37f);}
            // Upper roof ventilation louvers are broad enough to remain stable at riding distance.
            for(int row=0;row<3;row++)B("Wood",0,2.98f+row*.09f,.64f,width,.035f,.055f);
            for(int i=0;i<11;i++)B("Frame",-10.5f+i*2.1f,3.10f,.65f,.055f,.31f,.07f);
        }
        static void Group(Transform parent,Survey survey,string id,int edgeIndex,string name,float centerZ,float width,Action build)
        {
            var building=survey.buildings.First(b=>b.id==id);var edge=building.roadFrontEdges.First(e=>e.roadId=="316698035"&&e.edgeIndex==edgeIndex);
            var direction=(edge.b-edge.a).normalized;
            float u=Mathf.Abs(direction.z)>.01f?(centerZ-edge.a.z)/direction.z:edge.lengthMetres*.5f;
            u=Mathf.Clamp(u,width*.5f+.03f,edge.lengthMetres-width*.5f-.03f);
            Vector3 origin=edge.a+direction*u+edge.outward*.065f;
            var go=new GameObject(name+" • OSM "+id);go.layer=2;go.transform.SetParent(parent,false);
            go.transform.localPosition=origin;go.transform.localRotation=Quaternion.LookRotation(edge.outward,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/"+id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();build();BakeLettering();SaveMeshes(go.transform);
        }
        public static GameObject Build(Transform parent,Vector3 origin,Quaternion rotation)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build the survey facade outside Play mode.");
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var old=parent?parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName)?.gameObject:SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(old)Undo.DestroyObjectImmediate(old);var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);root.transform.SetPositionAndRotation(origin,rotation);
            Undo.RegisterCreatedObjectUndo(root,"Build dated street facade crops");
            Group(root.transform,survey,"1287937528",1,"SF01 • 235 brothers and 237 tofu",59.85f,8.1f,BrothersAndTofu);
            Group(root.transform,survey,"438606733",2,"SF02 • 251 corner terrace and street fronts",12.31f,13.25f,Corner251);
            Group(root.transform,survey,"366231799",2,"SF03 • Kingstone basement entry roof",-111.20f,21.82f,Bookstore);
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
            var fineSurfaces=new HashSet<string>{"Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                var g=pair.Value;string path=Art+"/Survey_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="Survey_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Survey facade batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
