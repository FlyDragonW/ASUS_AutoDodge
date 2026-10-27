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
    // See Sources/south-continuation-2025.json; January2025 filming is author-reported.
    // The unmapped257 frontage is separate from the bounded261 footprint slice.
    public static class SouthContinuationSetup
    {
        public const string RootName = "South continuation • filmed January 2025";
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
            M("Wall","#8C8880",0,.17f);M("Floor","#888984",0,.17f);M("Inside","#CCC6B8",0,.18f);
            M("Frame","#333634",.18f,.29f);M("Steel","#89918C",.42f,.39f);M("GlassDark","#252C29",.06f,.41f);
            M("Paper","#EBE9DD",0,.22f);M("Ink","#181B1B",0,.20f);M("Wood","#8C6950",0,.20f);
            M("WoodDark","#674C3B",0,.17f);M("Red","#C64429",0,.22f);M("LedRed","#EA3925",0,.22f);
            M("Gold","#C59A53",0,.23f);M("FoodGreen","#759A5D",0,.2f);M("FoodBrown","#845A37",0,.2f);
            M("FamilyGreen","#40A948",0,.22f);M("FamilyBlue","#24618B",0,.21f);M("CafeBlue","#26364D",0,.22f);
            M("PackageBlue","#698E9E",0,.20f);M("PackagePink","#BC8882",0,.20f);M("Light","#EDEAD5",0,.22f);
            foreach(string key in new[]{"Light","LedRed"}){
                var m=materials[key];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(key=="Light"?"#ABA68A":"#E33120").linear*.45f);
            }
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.075f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.045f,fontHeight,ink,w-.14f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.012f),new Vector3(x,y,z+.012f),r,24); }
        static void Shell(float ceiling,float depth,string inside="Inside")
        {
            // Only the witnessed shallow entrance is represented. No upper storeys or floorplan are inferred.
            B("Floor",0,.055f,-depth*.5f+.15f,width,.11f,depth+.5f);
            B(inside,0,ceiling*.5f,-depth,width,ceiling,.09f);
            B(inside,0,ceiling,-depth*.5f,width,.11f,depth);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.05f),ceiling*.5f,-depth*.5f,.10f,ceiling,depth);
        }
        static void Glass(float x,float w,float z,float h=2.63f)
        {
            B("StoreGlass",x,h*.5f+.16f,z,w,h,.020f);
            foreach(float dx in new[]{-w*.5f,w*.5f})B("Steel",x+dx,h*.5f+.16f,z+.024f,.039f,h+.045f,.058f);
            foreach(float yy in new[]{.16f,h+.16f})B("Steel",x,yy,z+.024f,w,.039f,.058f);
        }
        static void MenuPanel(float x,float y,float z,float w,float h)
        {
            B("Steel",x,y,z,w,h,.07f);B("Ink",x,y,z+.04f,w-.06f,h-.06f,.013f);
            for(int row=0;row<2;row++){
                float py=y+h*(.23f-row*.46f);
                Circle("Paper",x,py,z+.052f,w*.35f);Circle("FoodBrown",x-.045f,py-.012f,z+.072f,w*.26f);
                Circle("Gold",x+w*.13f,py+.075f,z+.087f,w*.08f);
                Circle("FoodGreen",x-w*.15f,py-.08f,z+.087f,w*.07f);
                for(int col=0;col<3;col++)B("Paper",x-w*.27f+col*w*.25f,py-h*.16f,z+.058f,w*.16f,.015f,.008f);
            }
        }
        static void SpatulaDisc(float x,float y,float z)
        {
            // Red disc and paired dark spatula silhouette are personally visible at January08:36.
            Rod("Paper",new Vector3(x-.053f,y,z),new Vector3(x+.053f,y,z),.46f,32);
            foreach(float side in new[]{-1f,1f}){
                float face=x+side*.062f;
                Rod("Red",new Vector3(face,y,z),new Vector3(face+side*.01f,y,z),.417f,32);
                foreach(float direction in new[]{-1f,1f}){
                    Rod("Ink",new Vector3(face+side*.019f,y+.28f,z-direction*.03f),new Vector3(face+side*.019f,y-.06f,z+direction*.15f),.019f,6);
                    B("Ink",face+side*.023f,y-.155f,z+direction*.15f,.012f,.23f,.18f);
                }
            }
        }
        static void Weiyu()
        {
            Shell(3.25f,3.20f,"WoodDark");
            B("Wood",0,3.18f,-1.56f,width-.20f,.11f,3.04f);
            for(int i=0;i<13;i++)B("WoodDark",-width*.46f+i*width*.92f/12,3.112f,-1.50f,.018f,.012f,2.91f);
            for(int i=0;i<5;i++)Circle("Light",-width*.39f+i*width*.195f,3.03f,-.06f,.061f);
            // A shallow visible single step, dark frames and a recessed glazed entrance.
            B("Wood",0,.11f,-1.32f,width-.22f,.13f,2.04f);
            B("Ink",0,3.38f,-.82f,width,.53f,.16f);
            Circle("Paper",width*.30f,3.38f,-.727f,.20f);Circle("FoodBrown",width*.30f,3.37f,-.707f,.145f);
            for(int col=0;col<6;col++)for(int row=0;row<3;row++)B("Paper",width*.08f-col*width*.074f,3.51f-row*.13f,-.730f,.038f,.045f,.010f);
            foreach(float side in new[]{-1f,1f})B("Ink",side*(width*.5f-.18f),1.57f,-.78f,.36f,3.14f,.45f);
            Glass(width*.16f,width*.50f,-1.61f,2.67f);B("Frame",width*.16f,1.48f,-1.58f,.05f,2.65f,.07f);
            foreach(float dx in new[]{-.12f,.12f})B("Steel",width*.16f+dx,1.33f,-1.534f,.022f,.42f,.039f);
            Glass(-width*.29f,width*.25f,-1.61f,2.67f);
            B("Wood",width*.12f,.78f,-2.72f,width*.48f,.06f,.42f);
            foreach(float xx in new[]{-width*.03f,width*.30f}){
                B("WoodDark",xx,.45f,-2.26f,.39f,.052f,.35f);
                foreach(float dx in new[]{-.15f,.15f})foreach(float dz in new[]{-.13f,.13f})B("Frame",xx+dx,.25f,-2.26f+dz,.024f,.40f,.024f);
            }
            // Pale structural jamb with neutral worn marks; graffiti is not transcribed.
            B("Wall",width*.5f-.22f,1.61f,-.025f,.40f,3.22f,.39f);
            for(int i=0;i<11;i++)B(i%3==0?"Paper":"Frame",width*.5f-.22f+(i%3-1)*.075f,.36f+i*.21f,.177f,.11f,.024f,.006f);
            float mx=-width*.36f;
            B("Ink",mx,1.17f,.015f,.92f,2.34f,.33f);MenuPanel(mx,1.90f,.197f,.88f,1.30f);
            L("威宇",mx,2.66f,.244f,.19f,"#E9E4D2",.75f);
            float sx=-width*.25f;
            // The footprint gap has no claimed upper facade. Keep the visible sign physically
            // attached to the existing canopy with a minimal estimated steel mounting frame.
            B("Frame",sx,4.84f,-.26f,.060f,3.22f,.070f);
            foreach(float mountY in new[]{4.08f,6.25f})B("Frame",sx,mountY,.24f,.060f,.045f,1.00f);
            B("Frame",sx,5.18f,.25f,.12f,2.63f,.66f);B("Ink",sx,5.18f,.65f,.18f,2.60f,.87f);
            foreach(float side in new[]{-1f,1f}){
                Label("威\n宇",new Vector3(sx+side*.10f,5.72f,.65f),.43f,"#E9E6D7",.73f,side*90f);
                Label("牛\n排",new Vector3(sx+side*.10f,4.95f,.65f),.20f,"#E9E6D7",.65f,side*90f);
                Label("Wei Yu",new Vector3(sx+side*.10f,4.51f,.65f),.12f,"#E9E6D7",.65f,side*90f);
                Rod("Red",new Vector3(sx+side*.104f,4.21f,.65f),new Vector3(sx+side*.115f,4.21f,.65f),.093f,16);
            }
            float discX=width*.21f;
            B("Frame",discX,3.975f,-.26f,.055f,1.55f,.065f);
            B("Frame",discX,4.74f,.245f,.050f,.045f,1.01f);
            SpatulaDisc(discX,4.76f,.70f);
            float ledX=-width*.46f;
            B("Frame",ledX,4.79f,-.26f,.052f,3.12f,.065f);
            foreach(float mountY in new[]{4.18f,6.19f})B("Frame",ledX,mountY,.235f,.050f,.038f,.99f);
            B("Ink",ledX,5.20f,.63f,.15f,2.41f,.54f);
            foreach(float side in new[]{-1f,1f})Label("巨\n大\n魷\n魚\n排",new Vector3(ledX+side*.084f,5.20f,.63f),.27f,"#FF8B3C",.43f,side*90f);
            // Unseen cooking areas, people and source food-photo pixels are deliberately absent.
        }
        static void FamilyBand(float centerY,float z,float bandWidth,bool street)
        {
            B("Paper",0,centerY,z,bandWidth,.51f,.13f);
            B("FamilyGreen",0,centerY+.33f,z+.02f,bandWidth,.16f,.16f);
            B("FamilyBlue",0,centerY-.30f,z+.02f,bandWidth,.10f,.16f);
            L(street?"FamilyMart":"全家 FamilyMart",0,centerY,z+.071f,street?.40f:.23f,"#1F557C",bandWidth-.43f);
        }
        static void Package(float x,float y,float z,int variant)
        {
            string mat=variant%4==0?"PackageBlue":variant%4==1?"PackagePink":variant%4==2?"FoodGreen":"Paper";
            B(mat,x,y+.115f,z,.14f,.23f,.13f);B("Paper",x,y+.14f,z+.074f,.10f,.054f,.010f);
        }
        static void FamilyMart()
        {
            Shell(3.23f,3.85f);
            B("Ink",0,3.48f,-.16f,width,.47f,.63f);FamilyBand(3.81f,.19f,width,true);
            // The rear shopfront and the higher outer arcade band are separate in January08:46.
            FamilyBand(3.00f,-1.42f,width-.38f,false);
            B("Paper",0,3.19f,-.75f,width,.13f,1.50f);
            for(int i=0;i<5;i++){
                Rod("Light",new Vector3(-width*.39f+i*width*.195f,3.115f,-.66f),new Vector3(-width*.39f+i*width*.195f,3.123f,-.66f),.072f,16);
                B("Light",-width*.38f+i*width*.19f,3.13f,-2.66f,.06f,.025f,1.84f);
            }
            float doorwayX=width*.27f;
            Glass(doorwayX,width*.35f,-1.43f,2.46f);B("Steel",doorwayX,1.37f,-1.407f,.041f,2.44f,.062f);
            Glass(-width*.20f,width*.51f,-1.43f,2.46f);
            // Visible dark blue lower window panel, simple cafe emblem, no source poster faces.
            B("CafeBlue",-width*.20f,.70f,-1.395f,width*.50f,1.01f,.045f);
            Circle("Gold",-width*.22f,.94f,-1.365f,.12f);Circle("CafeBlue",-width*.22f,.94f,-1.349f,.084f);
            L("Let's Café",-width*.22f,.70f,-1.362f,.18f,"#E8E8DA",width*.32f);
            B("Gold",width*.45f,.95f,-1.36f,.30f,1.53f,.15f);
            for(int row=0;row<4;row++){
                float y=.36f+row*.43f;B("Paper",0,y,-3.64f,width-.48f,.035f,.30f);
                for(int col=0;col<16;col++)Package(-width*.42f+col*width*.84f/15,y+.022f,-3.49f,row+col);
            }
            B("Wood",-width*.19f,1.10f,-2.27f,width*.46f,.13f,.54f);
            B("Paper",width*.03f,2.08f,-1.464f,width*.34f,.53f,.025f);
            for(int i=0;i<3;i++)B(i%2==0?"PackagePink":"Gold",width*.13f-i*width*.09f,2.08f,-1.443f,width*.055f,.29f,.009f);
            // One witnessed dark arcade pillar bounds the convenience frontage. The long cinema
            // poster colonnade farther south is outside this group and must remain unclaimed.
            float columnX=-width*.5f+.44f;
            B("Ink",columnX,1.64f,-.06f,.86f,3.28f,.82f);
            Panel("Frame",columnX,1.92f,.365f,.62f,1.14f,"","#FFFFFF",.1f);
            B("Paper",columnX,1.92f,.411f,.53f,1.04f,.010f);
            for(int row=0;row<4;row++)for(int col=0;col<2;col++)B(row%2==0?"PackagePink":"PackageBlue",columnX-.13f+col*.26f,1.62f+row*.20f,.424f,.15f,.13f,.006f);
        }
        public static Vector3 EstimatedWeiyuOutward()=>new Vector3(-.95749153f,0,.28846138f);
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build south continuation in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("SC25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            if(entries.Length!=2||!new[]{"SC2501","SC2502"}.SequenceEqual(entries.Select(e=>e.id)))throw new InvalidOperationException("Merge exactly SC2501 and SC2502 reservations before building.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January 2025 south continuation");
            foreach(var entry in entries){
                Vector3 normal;
                if(entry.id=="SC2501"){
                    if(!string.IsNullOrEmpty(entry.buildingId))throw new InvalidOperationException("Weiyu257 is an explicitly unmapped frontage.");
                    normal=EstimatedWeiyuOutward();
                }else{
                    if(entry.buildingId!="228711106"||entry.edgeIndex!=0)throw new InvalidOperationException("FamilyMart261 uses the north part of existing footprint228711106 edge0.");
                    normal=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==0&&e.roadId=="316698035").outward.normalized;
                }
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/SouthContinuation/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                if(entry.id=="SC2501")Weiyu();else FamilyMart();
                BakeLettering();float top=geometry.Values.SelectMany(g=>g.vertices).Max(v=>v.y);
                if(top>Mathf.Max(entry.height,entry.visualHeight)+.025f)throw new InvalidOperationException(entry.id+" exceeds visualHeight.");
                SaveMeshes(go.transform);
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
            string path=Art+"/SouthContinuationLetteringAtlas.png";
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
                var g=pair.Value;string path=Art+"/SouthContinuation_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="SouthContinuation_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("South continuation batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=pair.Key!="Inside"&&pair.Key!="Floor";var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
