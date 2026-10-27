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
    // See Sources/mid-west-retail-2025.json; January filming is author-reported; dimensions are estimates.
    public static class MidWestRetailSetup
    {
        public const string RootName = "Mid-west retail • filmed January 2025";
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
            M("Wall","#A09C91",0,.16f);M("Floor","#AAA89B",0,.15f);M("Inside","#C9C8BA",0,.18f);
            M("Frame","#343C38",.14f,.24f);M("Steel","#8F9C95",.45f,.34f);M("GlassDark","#314340",.08f,.43f);
            M("Paper","#E8E4D7",0,.18f);M("Ink","#1B2721",0,.18f);M("Wood","#9C8060",0,.20f);
            M("Light","#ECE4C8",0,.23f);M("Red","#AE4947",0,.20f);M("TelecomBlue","#2E608F",0,.20f);
            M("MilkCyan","#269AB3",0,.21f);M("NeonWhite","#E2EBE6",0,.20f);M("NeonBlue","#65BFCC",0,.21f);
            M("ClothPink","#B98C99",0,.16f);M("ClothCream","#D4C1A7",0,.16f);M("PaleMetal","#ADB5AE",.32f,.29f);
            foreach(string key in new[]{"Light","NeonWhite","NeonBlue"}){
                var m=materials[key];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(key=="NeonBlue"?"#2A9CAE":"#ACB5A1")*.36f);
            }
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        {B(mat,x,y,z,w,h,.077f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.045f,fontHeight,ink,w-.15f);}
        static void Circle(string mat,float x,float y,float z,float radius)
        {Rod(mat,new Vector3(x,y,z-.013f),new Vector3(x,y,z+.013f),radius,24);}
        static void Shell(float h=3.09f,float depth=2.12f)
        {
            // Footage resolves frontage and a shallow merchandise/counter strip, not full interiors.
            B("Floor",0,.045f,-depth*.45f,width,.09f,depth+.38f);B("Inside",0,h*.5f,-depth,width,h,.08f);
            B("Inside",0,h,-depth*.45f,width,.10f,depth+.38f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.046f),h*.5f,-depth*.49f,.092f,h,depth);
            for(int i=0;i<3;i++)B("Light",(i-1)*width*.27f,h-.075f,-depth*.43f,.055f,.023f,depth*.77f);
        }
        static void Glass(float x,float w,float h=2.72f,string frame="Frame")
        {
            B("StoreGlass",x,h*.5f+.13f,-.10f,w,h,.022f);
            foreach(float dx in new[]{-w*.5f,0f,w*.5f})B(frame,x+dx,h*.5f+.13f,-.066f,.032f,h+.07f,.063f);
            foreach(float y in new[]{.13f,h+.13f})B(frame,x,y,-.066f,w,.04f,.063f);
        }
        static void TurningPasta()
        {
            Shell(3.12f,1.84f);
            Panel("Ink",0,3.62f,.09f,width,.91f,"","#FFFFFF",.1f);
            L("轉轉",-width*.09f,3.74f,.136f,.45f,"#EBE9D9",width*.59f);
            // Fine fascia tagline is blurred in the oblique footage; do not fabricate its layout.
            Circle("Paper",width*.365f,3.73f,.137f,.224f);Circle("Inside",width*.365f,3.73f,.159f,.175f);
            // Inner mascot strokes are unresolved, so the observed pale disc is kept neutral.
            foreach(float side in new[]{-1f,1f})B("Paper",side*(width*.5f-.055f),1.56f,-.015f,.11f,3.10f,.28f);
            Glass(0,width-.33f,2.73f,"Steel");B("Steel",-.09f,1.34f,-.016f,.018f,.43f,.026f);
            B("Paper",0,1.48f,-1.78f,width-.25f,2.72f,.055f);
            B("Wood",0,.75f,-1.29f,width*.65f,.06f,.39f);B("Wood",0,.52f,-1.60f,width*.72f,.51f,.19f);
            for(int i=0;i<3;i++){
                float x=-width*.23f+i*width*.23f;Panel("Paper",x,2.04f,-1.72f,width*.19f,.66f,"","#FFFFFF",.1f);
                Circle(i%2==0?"Wood":"Red",x,2.06f,-1.676f,width*.057f);
            }
            float sx=-width*.5f+.14f;
            B("Frame",sx,5.27f,.18f,.12f,2.21f,.57f);B("Ink",sx,5.27f,.55f,.17f,2.19f,.61f);
            foreach(float side in new[]{-1f,1f})Label("轉\n轉",new Vector3(sx+side*.092f,5.55f,.55f),.35f,"#E8E7DB",.48f,side*90f);
            // Small characters on the high sign are not reliable at this viewing distance.
        }
        static void WingEmblem(float x,float y,float z)
        {
            // Original simple paired chevrons identify the observed red wing-like mark.
            foreach(float side in new[]{-1f,1f}){
                var g=G("Red");
                var a=new Vector3(x,y-.10f,z);var b=new Vector3(x+side*.21f,y-.05f,z);var c=new Vector3(x+side*.37f,y+.20f,z);var d=new Vector3(x+side*.21f,y+.17f,z);
                if(side>0)g.Quad(a,b,c,d);else g.Quad(d,c,b,a);
                a=new Vector3(x+side*.03f,y-.13f,z+.002f);b=new Vector3(x+side*.18f,y-.12f,z+.002f);c=new Vector3(x+side*.27f,y+.03f,z+.002f);d=new Vector3(x+side*.14f,y+.02f,z+.002f);
                if(side>0)g.Quad(a,b,c,d);else g.Quad(d,c,b,a);
            }
        }
        static void Hanger(float x,float y,float z,int variation)
        {
            Rod("Steel",new Vector3(x,y,z),new Vector3(x-.09f,y-.08f,z),.008f,6);Rod("Steel",new Vector3(x,y,z),new Vector3(x+.09f,y-.08f,z),.008f,6);
            Rod("Steel",new Vector3(x-.09f,y-.08f,z),new Vector3(x+.09f,y-.08f,z),.008f,6);
            // Product silhouettes only; exact garments are not recoverable from the source.
            B(variation%3==0?"ClothPink":variation%3==1?"ClothCream":"Paper",x,y-.24f,z,.16f,.31f,.025f);
        }
        static void Wacoal()
        {
            Shell(3.09f,2.2f);
            Panel("Paper",0,3.61f,.09f,width,.84f,"","#FFFFFF",.1f);
            L("華歌爾",-width*.07f,3.61f,.137f,.48f,"#A74746",width*.59f);WingEmblem(width*.36f,3.59f,.138f);
            foreach(float side in new[]{-1f,1f})B("Paper",side*(width*.5f-.073f),1.56f,.015f,.146f,3.1f,.31f);
            Glass(0,width-.36f,2.74f,"PaleMetal");
            B("Red",-width*.37f,1.65f,.016f,width*.17f,2.49f,.10f);
            for(int i=0;i<6;i++)B("Paper",-width*.37f,.65f+i*.37f,.071f,width*.10f,.022f,.009f);
            for(int row=0;row<2;row++){
                float y=2.33f-row*.91f;Rod("Steel",new Vector3(-width*.40f,y,-2.07f),new Vector3(width*.40f,y,-2.07f),.015f,8);
                for(int col=0;col<10;col++)Hanger(-width*.36f+col*width*.72f/9,y-.025f,-2.06f,row+col);
            }
            B("Paper",width*.25f,.69f,-.70f,width*.23f,1.19f,.61f);B("GlassDark",width*.25f,1.40f,-.70f,.26f,.27f,.18f);
            // Unreadable sale copy and model/product photography are not fabricated.
        }
        static void Telecom()
        {
            Shell(3.09f,2.24f);
            Panel("TelecomBlue",0,3.66f,.085f,width,.79f,"","#FFFFFF",.1f);
            L("名立時尚",-width*.04f,3.65f,.131f,.40f,"#E3E9E2",width*.55f);
            for(int i=0;i<3;i++)Box("Paper",new Vector3(width*.365f+i*.098f,3.66f,.132f),new Vector3(.067f,.30f,.009f),Quaternion.Euler(0,0,-23));
            // Fine slogan across the long blue band is blurred; retain clean blue space.
            B("Paper",0,3.15f,.055f,width,.16f,.34f);
            foreach(float x in new[]{-width*.5f+.082f,width*.5f-.082f,0f})B("Paper",x,1.55f,-.01f,.164f,3.09f,.34f);
            Glass(width*.23f,width*.42f,2.70f,"Steel");Glass(-width*.23f,width*.42f,2.70f,"Steel");
            B("Paper",0,1.52f,-2.17f,width-.35f,2.70f,.08f);
            for(int row=0;row<3;row++)for(int col=0;col<12;col++){
                float x=-width*.40f+col*width*.80f/11,y=.60f+row*.57f;
                B("Paper",x,y,-2.11f,.28f,.42f,.052f);B("GlassDark",x,y,-2.075f,.16f,.28f,.014f);
            }
            B("Paper",width*.15f,.69f,-.87f,width*.63f,1.19f,.67f);B("PaleMetal",width*.15f,1.31f,-.83f,width*.65f,.049f,.77f);
            for(int i=0;i<5;i++)Box("GlassDark",new Vector3(-width*.05f+i*width*.10f,1.385f,-.80f),new Vector3(.085f,.14f,.018f),Quaternion.Euler(28,0,0));
            B("TelecomBlue",-width*.5f+.30f,1.83f,.027f,.42f,1.56f,.055f);B("Paper",-width*.5f+.30f,1.91f,.061f,.30f,.91f,.018f);
        }
        static void BottleOutline(float x,float centerY,float centerZ,float side)
        {
            // Original geometric outline of the visible white milk-bottle symbol; no invented text.
            Vector2[] points={new Vector2(-.135f,.63f),new Vector2(.135f,.63f),new Vector2(.135f,.39f),new Vector2(.23f,.29f),new Vector2(.23f,-.43f),new Vector2(.17f,-.51f),new Vector2(-.17f,-.51f),new Vector2(-.23f,-.43f),new Vector2(-.23f,.29f),new Vector2(-.135f,.39f)};
            for(int i=0;i<points.Length;i++){
                var a=points[i];var b=points[(i+1)%points.Length];
                Rod("NeonWhite",new Vector3(x+side*.059f,centerY+a.y,centerZ+a.x),new Vector3(x+side*.059f,centerY+b.y,centerZ+b.x),.027f,8);
            }
            Rod("NeonWhite",new Vector3(x+side*.060f,centerY+.43f,centerZ-.125f),new Vector3(x+side*.060f,centerY+.43f,centerZ+.125f),.018f,6);
            // Small symbol inside the bottle is too blurred to resolve.
        }
        static void MilkTeaCrop()
        {
            Shell(3.08f,1.95f);
            Panel("MilkCyan",0,3.56f,.075f,width,.82f,"","#FFFFFF",.1f);
            Panel("Paper",0,3.57f,.124f,width*.65f,.59f,"","#FFFFFF",.1f);
            foreach(float side in new[]{-1f,1f})B("MilkCyan",side*(width*.5f-.10f),1.54f,.008f,.20f,3.07f,.34f);
            B("MilkCyan",0,.63f,-.31f,width-.39f,1.11f,.60f);B("PaleMetal",0,1.24f,-.26f,width-.29f,.075f,.80f);
            B("Paper",width*.22f,2.12f,-1.73f,width*.34f,1.26f,.08f);
            for(int row=0;row<7;row++)for(int col=0;col<3;col++)B("Ink",width*.08f+col*width*.13f,1.72f+row*.13f,-1.682f,width*.068f,.016f,.006f);
            B("StoreGlass",0,2.09f,-.095f,width-.44f,1.44f,.022f);
            foreach(float x in new[]{-width*.5f+.22f,0f,width*.5f-.22f})B("PaleMetal",x,2.07f,-.067f,.030f,1.52f,.047f);
            for(int i=0;i<6;i++)Rod("Paper",new Vector3(-width*.30f+i*.17f,1.29f,-.31f),new Vector3(-width*.30f+i*.17f,1.47f,-.31f),.049f,10);
            B("GlassDark",-width*.30f,1.49f,-.32f,.27f,.28f,.17f);
            float sx=-width*.5f+.18f;
            B("Frame",sx,5.38f,.22f,.08f,1.42f,.66f);B("Ink",sx,5.38f,.58f,.094f,1.36f,.68f);
            foreach(float side in new[]{-1f,1f})BottleOutline(sx,5.31f,.58f,side);
            // January2025 fascia is bright/blurred: do not print the later2026 business name or guess this text.
        }
        public static Vector3 FacadeOutward()
        {
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            return survey.buildings.First(b=>b.id=="767534280").roadFrontEdges.First(e=>e.edgeIndex==3&&e.roadId=="316698035").outward;
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build shop studies in Edit mode.");if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("MW25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            if(entries.Length!=4||!new[]{"MW2501","MW2502","MW2503","MW2504"}.SequenceEqual(entries.Select(e=>e.id)))throw new InvalidOperationException("Merge the four MW25 reservations before building mid-west retail.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January2025 mid-west retail");
            Vector3 normal=FacadeOutward();
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/MidWestRetail/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                switch(entry.id){case "MW2501":TurningPasta();break;case "MW2502":Wacoal();break;case "MW2503":Telecom();break;case "MW2504":MilkTeaCrop();break;}
                // A logo-only entry legitimately has no readable lettering.
                if(lettering.Count>0)BakeLettering();
                float top=geometry.Values.SelectMany(g=>g.vertices).Max(v=>v.y);
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
            string path=Art+"/MidWestRetailLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"TelecomBlue","MilkCyan","NeonWhite","NeonBlue","ClothPink","ClothCream","PaleMetal","WoodDark","CaneGreen","CaneDark","WishGreen","ToyBlue","ToyPink","StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/MidWestRetail_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="MidWestRetail_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Mid-west retail batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
