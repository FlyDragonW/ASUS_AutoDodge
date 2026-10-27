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
    // See Sources/sports-continuation-2025.json; January2025 filming is author-reported.
    // A visibly continuous sports frontage is retained without attributing a new business or address.
    public static class SportsContinuationSetup
    {
        public const string RootName = "Sports continuation • filmed January 2025";
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
            M("Wall","#B2B4AF",0,.17f);M("Floor","#B2B0A6",0,.18f);M("Inside","#D5D0C1",0,.19f);
            M("Frame","#313634",.14f,.30f);M("Steel","#969E99",.43f,.40f);M("GlassDark","#293531",.06f,.43f);
            M("Paper","#F0EDDE",0,.22f);M("Ink","#202524",0,.2f);M("Wood","#9B876B",0,.18f);
            M("Orange","#D7772D",0,.23f);M("Cream","#D7D0A9",0,.23f);M("BrandBlue","#234773",0,.24f);
            M("ClothBlue","#315789",0,.12f);M("ClothNavy","#283945",0,.12f);M("ClothLime","#ADC44C",0,.12f);
            M("ClothRose","#B97E82",0,.12f);M("Rubber","#D5D4C9",0,.17f);M("Light","#EFEBD7",0,.22f);
            materials["Light"].EnableKeyword("_EMISSION");materials["Light"].SetColor("_EmissionColor",Hex("#ACA990").linear*.40f);
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.09f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.051f,fontHeight,ink,w-.15f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.01f),new Vector3(x,y,z+.01f),r,20); }
        static void Frame(Action draw,Vector3 origin,Quaternion rotation)
        {
            var counts=geometry.ToDictionary(p=>p.Key,p=>p.Value.vertices.Count);int labelStart=lettering.Count;
            draw();
            foreach(var batch in geometry){int from=counts.TryGetValue(batch.Key,out var n)?n:0;
                for(int i=from;i<batch.Value.vertices.Count;i++)batch.Value.vertices[i]=origin+rotation*batch.Value.vertices[i];}
            for(int i=labelStart;i<lettering.Count;i++){lettering[i].position=origin+rotation*lettering[i].position;lettering[i].yaw+=rotation.eulerAngles.y;}
        }
        static void BirdMark(float x,float y,float z,float scale)
        {
            // Small original two-wing silhouette for the witnessed Mizuno sign, not a bitmap trace.
            // Positive local X appears on image-left; the longer wing points image-right.
            var g=G("Paper");
            Vector2[] p={new Vector2(-.29f,.05f),new Vector2(-.10f,-.01f),new Vector2(.32f,.14f),new Vector2(.18f,-.005f),new Vector2(-.045f,-.08f),new Vector2(-.17f,-.04f)};
            Vector3 P(Vector2 v)=>new Vector3(x-v.x*scale,y+v.y*scale,z);
            for(int i=1;i<p.Length-1;i++)g.Quad(P(p[0]),P(p[i+1]),P(p[i]),P(p[i]));
        }
        static void LeapingCat(float x,float y,float z)
        {
            // Coarse original white silhouette; the source only resolves the small leaping-cat mark.
            Vector3 P(float u,float v)=>new Vector3(x-u,y+v,z);
            Rod("Paper",P(-.13f,-.035f),P(.095f,.060f),.043f,8);
            Circle("Paper",x-.15f,y+.12f,z,.049f);
            Rod("Paper",P(.08f,.055f),P(.23f,.03f),.015f,6);Rod("Paper",P(.23f,.03f),P(.285f,.10f),.013f,6);
            Rod("Paper",P(.07f,.035f),P(.205f,-.04f),.014f,6);
            Rod("Paper",P(-.12f,-.04f),P(-.26f,-.15f),.015f,6);Rod("Paper",P(-.26f,-.15f),P(-.34f,-.15f),.012f,6);
            Rod("Paper",P(-.10f,-.02f),P(-.25f,.01f),.018f,6);
            Rod("Paper",P(-.25f,.01f),P(-.29f,.11f),.014f,6);Rod("Paper",P(-.29f,.11f),P(-.24f,.19f),.011f,6);
        }
        static void Garment(float x,float y,float z,int index)
        {
            string mat=index%6==0?"ClothLime":index%6==1?"ClothRose":index%6==2?"ClothBlue":index%6==3?"Paper":index%6==4?"ClothNavy":"Ink";
            Rod("Steel",new Vector3(x,y+.16f,z),new Vector3(x-.18f,y+.06f,z),.007f,6);
            Rod("Steel",new Vector3(x,y+.16f,z),new Vector3(x+.18f,y+.06f,z),.007f,6);
            B(mat,x,y-.27f,z,.33f,.65f,.09f);
            foreach(float side in new[]{-1f,1f})Box(mat,new Vector3(x+side*.22f,y-.12f,z),new Vector3(.12f,.44f,.09f),Quaternion.Euler(0,0,side*18));
            B(index%3==0?"ClothNavy":mat,x,y-.53f,z+.048f,.30f,.12f,.012f);
            if(index%4==0)B("Paper",x+.08f,y-.06f,z+.052f,.09f,.06f,.012f);
        }
        static void ClothingRail(float x,float z,float w,int variation)
        {
            float y=1.69f;
            Rod("Steel",new Vector3(x-w*.5f,y,z),new Vector3(x+w*.5f,y,z),.018f,8);
            foreach(float side in new[]{-1f,1f}){
                Rod("Frame",new Vector3(x+side*w*.5f,.20f,z),new Vector3(x+side*w*.5f,y,z),.019f,8);
                Rod("Frame",new Vector3(x+side*w*.5f,.17f,z-.27f),new Vector3(x+side*w*.5f,.17f,z+.27f),.020f,8);
                foreach(float dz in new[]{-.23f,.23f})Rod("Ink",new Vector3(x+side*w*.5f-.018f,.13f,z+dz),new Vector3(x+side*w*.5f+.018f,.13f,z+dz),.039f,10);
            }
            int count=Mathf.Max(4,Mathf.FloorToInt(w/.26f));
            for(int i=0;i<count;i++)Garment(x-w*.39f+i*w*.78f/(count-1),y-.18f,z,variation+i);
        }
        static void Shoe(float x,float y,float z,int index)
        {
            string upper=index%3==0?"ClothBlue":index%3==1?"Paper":"Ink";
            B("Rubber",x,y+.025f,z,.17f,.05f,.33f);B(upper,x,y+.092f,z+.018f,.15f,.09f,.29f);
            B(upper,x,y+.15f,z-.067f,.13f,.085f,.12f);
            for(int i=0;i<3;i++)B("Paper",x,y+.144f,z+.018f+i*.028f,.069f,.009f,.012f);
        }
        static void SportsFacade()
        {
            const float depth=3.18f;
            B("Floor",0,.075f,-1.48f,width,.12f,3.46f);
            B("Inside",0,1.57f,-depth,width,3.14f,.09f);
            B("Inside",0,3.12f,-1.50f,width,.12f,3.12f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.06f),1.58f,-1.50f,.12f,3.16f,3.10f);
            // Source08:23/26/28 shows the orange canopy continuing into a cream sports-brand board.
            float split=-width*.15f;
            B("Orange",width*.175f,3.66f,.08f,width*.65f,.94f,.22f);
            B("Cream",-width*.325f,3.66f,.08f,width*.35f,.94f,.22f);
            B("Frame",0,3.165f,.04f,width,.07f,.24f);
            L("SKECHERS",-width*.325f,3.52f,.199f,.28f,"#284A78",width*.32f);
            L("Mizuno",-width*.325f,3.92f,.199f,.17f,"#2D4C72",width*.28f);
            // Incomplete small canopy lettering is left as neutral marks, not a new store identity.
            for(int i=0;i<9;i++)B("Ink",width*.44f-i*width*.042f,3.70f,.196f,.045f,.13f,.010f);
            // Grey outer columns and round canopy lamps are distinct from the recessed black PUMA fascia.
            foreach(float x in new[]{width*.5f-.18f,split,-width*.5f+.16f}){
                B("Wall",x,1.59f,-.06f,.32f,3.18f,.43f);
                B("Steel",x,.13f,.164f,.33f,.02f,.016f);
            }
            for(int i=0;i<10;i++)Rod("Light",new Vector3(-width*.44f+i*width*.88f/9,3.037f,-.22f),new Vector3(-width*.44f+i*width*.88f/9,3.045f,-.22f),.068f,16);
            float pumaCenter=width*.175f,pumaWidth=width*.59f;
            Panel("Ink",pumaCenter,2.78f,-1.58f,pumaWidth,.51f,"PUMA","#ECEADB",.23f);
            LeapingCat(pumaCenter-.70f,2.82f,-1.528f);
            // Source clothing racks project into the shallow shop entrance; no carts or street stalls are inferred.
            ClothingRail(width*.32f,-.67f,width*.27f,0);
            ClothingRail(width*.005f,-.79f,width*.23f,3);
            ClothingRail(width*.21f,-2.13f,width*.35f,7);
            B("GlassDark",pumaCenter,1.50f,-3.119f,pumaWidth,2.70f,.018f);
            foreach(float x in new[]{pumaCenter-pumaWidth*.40f,pumaCenter+pumaWidth*.40f, pumaCenter})B("Frame",x,1.45f,-3.100f,.042f,2.68f,.035f);
            for(int row=0;row<4;row++){
                float y=.37f+row*.48f;
                B("Wood",-width*.325f,y,-2.85f,width*.27f,.035f,.44f);
                for(int col=0;col<6;col++)Shoe(-width*.43f+col*width*.042f,y+.021f,-2.79f,row+col);
            }
            // Small Mizuno blade is supported directly from the observed canopy/column, not floating.
            float bladeX=split-.15f;
            B("Frame",bladeX,3.105f,.36f,.06f,.08f,.90f);
            B("Frame",bladeX,2.99f,.72f,.08f,.35f,.065f);
            B("Paper",bladeX,2.89f,.78f,.13f,.58f,.64f);
            foreach(float side in new[]{-1f,1f})Frame(()=>{
                B("BrandBlue",0,0,.011f,.57f,.51f,.018f);
                BirdMark(0,.10f,.025f,.72f);
                L("Mizuno",0,-.11f,.026f,.105f,"#EEEDE2",.47f);
            },new Vector3(bladeX+side*.074f,2.89f,.78f),Quaternion.Euler(0,side*90f,0));
        }
        public static Vector3 EstimatedOutward()=>new Vector3(-.95749153f,0,.28846138f);
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build sports continuation in Edit mode.");if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id=="SP2501").ToArray();
            if(entries.Length!=1)throw new InvalidOperationException("Merge SP2501 reservation before building sports continuation.");
            var entry=entries[0];if(!string.IsNullOrEmpty(entry.buildingId))throw new InvalidOperationException("SP2501 is an explicitly unmapped visible continuation, not a new measured footprint.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build observed sports facade continuation");
            var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(EstimatedOutward(),Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/SportsContinuation/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;SportsFacade();BakeLettering();
            float top=geometry.Values.SelectMany(g=>g.vertices).Max(v=>v.y);if(top>Mathf.Max(entry.height,entry.visualHeight)+.025f)throw new InvalidOperationException("SP2501 exceeds its visualHeight.");
            SaveMeshes(go.transform);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);return root;
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
            string path=Art+"/SportsContinuationLetteringAtlas.png";
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
                var g=pair.Value;string path=Art+"/SportsContinuation_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="SportsContinuation_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Sports continuation batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
