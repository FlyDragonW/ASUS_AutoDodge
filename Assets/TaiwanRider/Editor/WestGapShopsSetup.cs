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
    public static class WestGapShopsSetup
    {
        public const string RootName = "West gap • observed telecom facade January 2025";
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
        static void TelecomAllianceCrop()
        {
            Shell(3.10f,1.92f);
            M("AllianceGreen","#2F847F");
            Panel("AllianceGreen",0,3.76f,.08f,width,.83f,"聯強電信聯盟","#EAEADA",.32f);
            L("2",-width*.39f,3.76f,.131f,.59f,"#E5D151",.34f);
            B("Ink",0,3.20f,.13f,width,.28f,.13f);
            // Do not invent the unreadable scrolling message; retain observed LED colour groups.
            for(int i=0;i<32;i++)B(i<17?"AllianceGreen":"Red",-width*.45f+i*width*.90f/31,3.20f,.202f,.050f,.08f,.008f);
            Panel("TelecomBlue",width*.35f,2.89f,.14f,width*.26f,.30f,"vivo","#FFFFFF",.24f);
            Glass(0,width-.30f,2.64f,"PaleMetal");
            for(int r=0;r<3;r++){
                float y=.67f+r*.56f;B("Paper",0,y,-1.62f,width-.45f,.045f,.33f);
                for(int i=0;i<7;i++){
                    float x=-width*.36f+i*width*.12f;
                    B("Ink",x,y+.155f,-1.60f,.18f,.28f,.035f);
                    B(i%2==0?"TelecomBlue":"Paper",x,y+.16f,-1.574f,.14f,.22f,.009f);
                }
            }
            B("Paper",0,.62f,-.82f,width*.44f,1.03f,.48f);
            float sx=-width*.5f+.16f;
            B("Frame",sx,6.15f,.19f,.11f,3.3f,.42f);
            B("AllianceGreen",sx,6.15f,.61f,.20f,3.20f,.71f);
            foreach(float side in new[]{-1f,1f}){
                Label("聯\n強\n電\n信\n聯\n盟",new Vector3(sx+side*.108f,6.42f,.61f),.22f,"#ECEBD9",.49f,side*90);
                Label("2",new Vector3(sx+side*.11f,4.93f,.61f),.36f,"#E0D153",.47f,side*90);
            }
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
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("WG25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            if(entries.Length!=1||entries[0].id!="WG2501")throw new InvalidOperationException("Merge the single WG25 observed facade reservation.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January2025 mid-west retail");
            Vector3 normal=FacadeOutward();
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/WestGapShops/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                TelecomAllianceCrop();
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
            string path=Art+"/WestGapShopsLetteringAtlas.png";
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
                var g=pair.Value;string path=Art+"/WestGapShops_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="WestGapShops_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
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
