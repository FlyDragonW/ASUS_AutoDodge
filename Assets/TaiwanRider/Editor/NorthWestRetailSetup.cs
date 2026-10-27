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
    // See Sources/north-west-retail-2025.json; January filming is author-reported, published 10 February 2025.
    public static class NorthWestRetailSetup
    {
        public const string RootName = "Northwest retail • filmed January 2025";
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
            M("Wall","#928D83",0,.16f);M("Floor","#AAA497",0,.16f);M("Inside","#CABFAC",0,.18f);
            M("Frame","#303734",.12f,.28f);M("Steel","#858E88",.45f,.38f);M("GlassDark","#33413C",.08f,.45f);
            M("Paper","#EAE7DC",0,.2f);M("Ink","#19231F",0,.22f);M("Wood","#A2784D",0,.2f);
            M("WoodDark","#745435",0,.18f);M("Light","#F0DBA9",0,.24f);M("Red","#B54331",0,.2f);
            M("CaneGreen","#288B42",0,.19f);M("CaneDark","#1F5738",0,.18f);
            M("WishGreen","#81AC34",0,.20f);M("ToyBlue","#587F91",0,.20f);M("ToyPink","#B77876",0,.2f);
            foreach(string key in new[]{"Light","Paper"}){
                var m=materials[key];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(key=="Light"?"#A99462":"#858878")*.25f);
            }
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.08f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.045f,fontHeight,ink,w-.16f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.014f),new Vector3(x,y,z+.014f),r,24); }
        static void Shell(float h=3.10f)
        {
            // Only a shallow visible retail bay; no claimed floorplan or upper storeys.
            B("Floor",0,.05f,-.97f,width,.10f,2.45f);B("Inside",0,h*.5f,-2.15f,width,h,.10f);
            B("Inside",0,h,-.97f,width,.12f,2.45f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.045f),h*.5f,-.98f,.09f,h,2.40f);
            for(int i=0;i<3;i++)B("Light",(i-1)*width*.25f,h-.10f,-.89f,.06f,.025f,1.8f);
        }
        static void Glass(float x,float w,float height=2.77f)
        {
            B("StoreGlass",x,height*.5f+.13f,-.11f,w,height,.022f);
            foreach(float dx in new[]{-w*.5f,w*.5f})B("Frame",x+dx,height*.5f+.13f,-.071f,.045f,height+.07f,.072f);
            foreach(float y in new[]{.13f,height+.13f})B("Frame",x,y,-.071f,w,.045f,.072f);
        }
        static void RetailToy(float x,float y,float z,int index)
        {
            // The footage resolves colourful boxes/figures, not individual licensed characters.
            string key=index%4==0?"ToyBlue":index%4==1?"ToyPink":index%4==2?"Paper":"Red";
            B(key,x,y+.103f,z,.16f,.206f,.13f);B("Paper",x,y+.052f,z+.07f,.119f,.027f,.009f);
            if(index%3==0){Circle("Paper",x,y+.148f,z+.073f,.046f);B("Ink",x,y+.15f,z+.091f,.045f,.008f,.008f);}
        }
        static void ToyShop()
        {
            Shell(3.16f);
            Panel("Ink",0,3.65f,.08f,width,.87f,"","#FFFFFF",.1f);
            L("辰TOY",width*.10f,3.65f,.126f,.61f,"#F4F1E6",width*.69f);
            Circle("Paper",-width*.405f,3.65f,.127f,.16f);Circle("GlassDark",-width*.405f,3.65f,.147f,.126f);
            // January 03:45 shows tiny white copy between TOY and the viewer-right disc.
            // Viewer-right is negative local X; preserve the observed density without inventing words.
            for(int i=0;i<7;i++)B("Paper",-width*(.266f+i*.014f),3.65f+(i%2)*.012f,.132f,width*.005f,.043f+(i%3)*.007f,.008f);
            B("Ink",0,3.15f,.025f,width,.14f,.30f);
            foreach(float side in new[]{-1f,1f})B("Ink",side*(width*.5f-.075f),1.56f,-.01f,.15f,3.11f,.33f);
            float doorWidth=width*.28f, doorX=-width*.13f;
            Glass(doorX,doorWidth);B("Steel",doorX+doorWidth*.31f,1.32f,-.007f,.02f,.52f,.04f);
            Glass(-width*.365f,width*.20f);Glass(width*.24f,width*.40f);
            B("Wood",0,1.38f,-2.04f,width-.35f,2.55f,.075f);
            for(int row=0;row<5;row++){
                float y=.34f+row*.47f;B("Light",0,y,-1.85f,width-.4f,.034f,.36f);
                int cols=Mathf.FloorToInt((width-.55f)/.27f);
                for(int col=0;col<cols;col++)RetailToy(-width*.5f+.34f+col*.27f,y+.02f,-1.79f,row*13+col);
            }
            B("Wood",width*.26f,.49f,-.57f,width*.36f,.76f,.43f);
            for(int i=0;i<5;i++)RetailToy(width*.10f+i*width*.065f,.89f,-.52f,i+1);
            // Small entrance flyers are visible, but their text is below source resolution.
            foreach(float side in new[]{-1f,1f})for(int i=0;i<3;i++)B("Paper",side*(width*.5f-.077f),1.09f+i*.36f,.162f,.11f,.18f,.008f);
        }
        static void Cup(float x,float y,float z,string key="Paper")
        {
            Rod(key,new Vector3(x,y,z),new Vector3(x,y+.17f,z),.048f,10);
            Rod("Paper",new Vector3(x,y+.175f,z),new Vector3(x,y+.188f,z),.052f,10);
        }
        static void CaneMother()
        {
            Shell(3.13f);
            Panel("CaneGreen",0,3.64f,.10f,width,.86f,"甘蔗媽媽","#F1F1DD",.44f);
            // Original neutral leaf-like silhouette approximates the bright icon; not a photo texture.
            Circle("Paper",width*.385f,3.65f,.146f,.16f);
            Rod("CaneGreen",new Vector3(width*.41f,3.54f,.165f),new Vector3(width*.36f,3.77f,.165f),.019f,6);
            B("WoodDark",0,3.13f,.08f,width,.16f,.29f);
            foreach(float side in new[]{-1f,1f})B("Wood",side*(width*.5f-.085f),1.57f,-.08f,.17f,3.13f,.34f);
            B("Wood",0,1.45f,-2.06f,width-.35f,2.69f,.075f);
            for(int i=0;i<13;i++)B("WoodDark",-width*.43f+i*width*.86f/12,1.45f,-2.014f,.018f,2.60f,.01f);
            B("Wood",-.08f,.65f,-.69f,width-.48f,1.19f,.72f);B("Inside",-.08f,1.285f,-.63f,width-.34f,.08f,.91f);
            for(int i=0;i<17;i++)B("WoodDark",-width*.435f+i*width*.84f/16,.64f,-.321f,.018f,1.15f,.01f);
            Panel("Paper",-width*.18f,2.03f,-1.76f,width*.40f,1.09f,"","#FFFFFF",.1f);
            for(int row=0;row<6;row++)for(int col=0;col<3;col++)B("WoodDark",-width*.32f+col*width*.12f,1.68f+row*.14f,-1.713f,width*.078f,.015f,.008f);
            for(int i=0;i<6;i++)Cup(-width*.28f+i*.15f,1.334f,-.56f);
            B("Ink",width*.25f,1.48f,-.58f,.32f,.30f,.22f);
            float sx=-width*.5f+.17f;
            B("Frame",sx,5.86f,.20f,.12f,2.24f,.65f);B("CaneDark",sx,5.86f,.67f,.18f,2.25f,.76f);
            B("CaneGreen",sx,6.58f,.67f,.187f,.80f,.77f);
            foreach(float side in new[]{-1f,1f}){
                Label("甘\n蔗\n媽\n媽",new Vector3(sx+side*.099f,5.69f,.67f),.30f,"#EDF1DC",.65f,side*90f);
                // Top emblem left as a simple white disc because internal portrait detail is unresolved.
                Rod("Paper",new Vector3(sx+side*.099f,6.60f,.67f),new Vector3(sx+side*.114f,6.60f,.67f),.23f,20);
            }
        }
        static void Wish()
        {
            Shell(3.09f);
            Panel("Paper",0,3.57f,.10f,width,.80f,"","#FFFFFF",.1f);
            L("MR.WISH",width*.065f,3.62f,.146f,.46f,"#548935",width*.73f);
            // Rounded green wordmark surround and lower green line are observed; fine slogan is unresolved.
            Circle("WishGreen",-width*.385f,3.62f,.147f,.24f);Circle("Paper",-width*.385f,3.62f,.168f,.187f);
            B("WishGreen",0,3.22f,.146f,width-.16f,.038f,.012f);
            foreach(float side in new[]{-1f,1f})B("Paper",side*(width*.5f-.067f),1.55f,-.03f,.134f,3.09f,.31f);
            B("Paper",0,.67f,-.66f,width-.35f,1.16f,.68f);B("Steel",0,1.31f,-.61f,width-.24f,.07f,.85f);
            B("Paper",0,2.77f,-.15f,width-.18f,.34f,.17f);
            for(int i=0;i<4;i++){
                float x=-width*.33f+i*width*.22f;Panel("Inside",x,2.77f,-.057f,width*.20f,.27f,"","#FFFFFF",.1f);
                Circle(i%2==0?"WishGreen":"Red",x-.10f,2.78f,-.009f,.047f);
            }
            B("Paper",0,1.78f,-1.98f,width-.30f,2.40f,.12f);
            for(int i=0;i<5;i++)Cup(-width*.28f+i*.19f,1.35f,-.49f);
            B("GlassDark",width*.27f,1.64f,-1.57f,.59f,.72f,.46f);
            for(int i=0;i<3;i++)B("Steel",width*.22f+i*.05f,1.61f,-1.325f,.017f,.32f,.028f);
            B("Ink",-width*.31f,1.49f,-.54f,.30f,.30f,.20f);
            float sx=-width*.5f+.22f;
            B("Frame",sx,5.43f,.34f,.085f,.075f,1.11f);
            Rod("WishGreen",new Vector3(sx-.087f,5.43f,.88f),new Vector3(sx+.087f,5.43f,.88f),.46f,32);
            foreach(float side in new[]{-1f,1f}){
                Label("Mr. Wish",new Vector3(sx+side*.094f,5.48f,.88f),.13f,"#F5F3E3",.78f,side*90f);
                // Source does not resolve the small circular-sign slogan; omit it.
            }
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build shop studies in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("NR25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            if(entries.Length!=3||!new[]{"NR2501","NR2502","NR2503"}.SequenceEqual(entries.Select(e=>e.id)))throw new InvalidOperationException("Merge the three NR25 reservations before building northwest retail.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January 2025 northwest retail");
            foreach(var entry in entries){
                string normalBuilding=string.IsNullOrEmpty(entry.buildingId)?"1287937529":entry.buildingId;
                int normalEdge=string.IsNullOrEmpty(entry.buildingId)?2:entry.edgeIndex;
                Vector3 normal=survey.buildings.First(b=>b.id==normalBuilding).roadFrontEdges.First(e=>e.edgeIndex==normalEdge&&e.roadId=="316698035").outward;
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/NorthWestRetail/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                switch(entry.id){case "NR2501":ToyShop();break;case "NR2502":CaneMother();break;case "NR2503":Wish();break;}
                BakeLettering();
                float top=geometry.Values.SelectMany(g=>g.vertices).Max(v=>v.y);
                if(top>Mathf.Max(entry.height,entry.visualHeight)+.025f)throw new InvalidOperationException(entry.id+" exceeds its visualHeight reservation.");
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
            string path=Art+"/NorthWestRetailLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"WoodDark","CaneGreen","CaneDark","WishGreen","ToyBlue","ToyPink","StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/NorthWestRetail_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="NorthWestRetail_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Northwest retail batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
