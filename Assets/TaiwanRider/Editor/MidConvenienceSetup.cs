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
    // See Sources/mid-convenience-2025.json; publication dates are separate from capture dates.
    public static class MidConvenienceSetup
    {
        public const string RootName = "Midstreet convenience • filmed January 2025";
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
        static void Materials()
        {
            M("Wall","#999184",0,.17f);M("Tile","#847266",0,.20f);M("Floor","#A5A398",0,.15f);
            M("Frame","#353B3A",.22f,.28f);M("Steel","#929A98",.5f,.40f);M("GlassDark","#344747",.15f,.5f);
            M("Inside","#BFBDB0",0,.20f);M("Paper","#E4DECC",0,.22f);M("Ink","#262F2E",0,.18f);
            M("Wood","#A38C6F",0,.18f);M("Light","#EFE7CB",0,.25f);M("Red","#C32B28",0,.23f);
            M("Green","#21885A",0,.20f);M("Orange","#D97129",0,.23f);M("Gold","#BFA458",.18f,.3f);
            M("Blue","#34597B",0,.21f);M("BlueBright","#306598",0,.26f);M("Cream","#D8C6A1",0,.21f);
            var tile=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TaiwanRider/Art/StreetSurvey/NorthWestShops/OriginalSmallTile.png");
            if(tile)materials["Tile"].SetTexture("_BaseMap",tile);
            // Daylight appearance reconstructed from night footage; avoid baking night exposure into geometry.
            foreach(string key in new[]{"Light","Paper"}){
                var m=materials[key];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex(key=="Light"?"#ADA88B":"#5A5A4F")*.35f);
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
        static void UpperPhotoWall(float w,float height,bool corner=false)
        {
            // Only the visible brown wall, window rows and parapets are studied. Back volume is neutral closure.
            B("Tile",0,(height+4.0f)*.5f,-.10f,w,height-4.0f,.22f);
            B("Wall",0,(height+3.2f)*.5f,-2.55f,w,height-3.2f,.10f);
            foreach(float side in new[]{-1f,1f})B("Tile",side*(w*.5f-.055f),(height+3.2f)*.5f,-1.28f,.11f,height-3.2f,2.55f);
            for(float y=5.04f;y<height-.8f;y+=2.55f){
                float ww=w*.73f;B("GlassDark",0,y,.035f,ww,1.46f,.035f);
                foreach(float xx in new[]{-ww*.5f,0,ww*.5f})B("Steel",xx,y,.075f,.045f,1.5f,.055f);
                foreach(float yy in new[]{y-.74f,y+.74f})B("Steel",0,yy,.075f,ww+.08f,.05f,.07f);
                B("Wall",0,y-.88f,.18f,w,.16f,.51f);
                for(int i=0;i<12;i++)B("Steel",-ww*.5f+i*ww/11,y-.28f,.30f,.018f,.76f,.022f);
                if(corner)B("Wall",0,y-.93f,.27f,w,.21f,.20f);
            }
        }
        static void Shoe(float x,float y,float z,int variation)
        {
            string upper=variation%4==0?"Red":variation%4==1?"Ink":variation%4==2?"Blue":"Paper";
            B("Paper",x,y,z,.22f,.024f,.09f);B(upper,x-.015f,y+.046f,z-.006f,.16f,.072f,.087f);
            B(upper,x+.087f,y+.025f,z,.073f,.035f,.087f);
            for(int i=0;i<3;i++)B("Steel",x-.024f+i*.024f,y+.086f,z,.008f,.009f,.056f);
        }
        static void ShoeInterior(float w)
        {
            // Visible rows of footwear; individual products are generic original mesh approximations.
            B("Wood",0,1.45f,-2.43f,w-.28f,2.7f,.06f);
            for(int row=0;row<5;row++){
                float y=.49f+row*.43f;B("Paper",0,y-.045f,-2.27f,w-.35f,.035f,.31f);
                int count=Mathf.Max(6,Mathf.FloorToInt((w-.6f)/.35f));
                for(int col=0;col<count;col++)Shoe(-w*.5f+.34f+col*(w-.68f)/(count-1),y,-2.25f,row*7+col);
            }
            foreach(float side in new[]{-1f,1f}){
                B("Wood",side*(w*.5f-.21f),1.36f,-1.21f,.10f,2.46f,2.2f);
                for(int row=0;row<4;row++)for(int col=0;col<4;col++)Shoe(side*(w*.5f-.32f),.62f+row*.44f,-.51f-col*.44f,row+col);
            }
            B("Paper",0,.59f,-1.37f,w*.34f,1.06f,.69f);
            for(int i=0;i<4;i++)Shoe(-w*.14f+i*w*.093f,1.13f,-1.34f,i);
        }
        static void AdidasBars(float x,float y,float z,float scale,string key="Paper")
        {
            for(int i=0;i<3;i++){
                float xx=x+(1-i)*.27f*scale, bottom=y-.22f*scale, h=(.23f+i*.22f)*scale, shift=h*.48f;
                G(key).Quad(new Vector3(xx-.10f*scale,bottom,z),new Vector3(xx+.10f*scale,bottom,z),
                    new Vector3(xx+.10f*scale+shift,bottom+h,z),new Vector3(xx-.10f*scale+shift,bottom+h,z));
            }
        }
        static void Seven()
        {
            Shell(width);
            B("Paper",0,3.49f,.07f,width,.81f,.25f);
            foreach(var stripe in new[]{("Orange",3.76f),("Green",3.54f),("Red",3.33f)})B(stripe.Item1,0,stripe.Item2,.204f,width,.105f,.025f);
            B("Paper",0,3.53f,.23f,.98f,1.07f,.045f);L("7",0,3.65f,.259f,.77f,"#C4482B",.68f);L("ELEVEN",0,3.22f,.261f,.13f,"#2B7658",.73f);
            float bay=(width-.63f)/3;
            for(int i=0;i<3;i++)GlassFront((i-1)*(bay+.05f),bay,2.73f);
            foreach(float x in new[]{-width*.5f+.12f,width*.5f-.12f})B("Frame",x,1.56f,.055f,.23f,3.03f,.41f);
            for(int row=0;row<5;row++){
                float y=.50f+row*.40f;B("Paper",0,y,-2.1f,width-.47f,.035f,.36f);
                for(int i=0;i<29;i++)B(i%4==0?"Orange":i%4==1?"Blue":i%4==2?"Paper":"Green",-width*.44f+i*width*.88f/28,y+.115f,-2.1f,.13f,.23f,.14f);
            }
            B("Paper",width*.29f,.67f,-1.1f,width*.23f,1.2f,.57f);B("Frame",width*.29f,1.43f,-1.08f,.35f,.31f,.24f);
            
            // The January footage shows a striped tall blade, rather than a red text-only box.
            float sx=width*.40f;
            B("Frame",sx,5.36f,.21f,.13f,3.55f,.56f);
            B("Paper",sx,5.36f,.62f,.20f,3.45f,.89f);
            foreach(float side in new[]{-1f,1f}){
                for(int i=0;i<3;i++)foreach(float y in new[]{4.04f,6.73f})
                    B(new[]{"Orange","Green","Red"}[i],sx+side*.108f,y,.37f+i*.25f,.012f,.60f,.095f);
                Label("7",new Vector3(sx+side*.118f,5.59f,.62f),.70f,"#BE3A26",.72f,side*90);
                Label("ELEVEN",new Vector3(sx+side*.120f,5.14f,.62f),.16f,"#247954",.77f,side*90);
            }
            foreach(float side in new[]{-1f,1f})B("Frame",side*width*.34f,.41f,-.042f,width*.29f,.57f,.05f);
            L("CITY PRIMA",width*.34f,.45f,-.007f,.17f,"#CAB589",2.10f);

        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build shop studies in Edit mode.");
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id.StartsWith("MC25")).ToArray();
            if(entries.Length!=1)throw new InvalidOperationException("Expected one January convenience shop reservation.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);
            Undo.RegisterCreatedObjectUndo(root,"Build 2025 walking-video shop studies");
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;
                Vector3 normal=new Vector3(-.99167065f,0,-.12879952f);
                if(!string.IsNullOrEmpty(entry.buildingId))normal=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==entry.edgeIndex&&e.roadId=="316698035").outward;
                go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/MidConvenience/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                Seven();
                BakeLettering();SaveMeshes(go.transform);
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
            string path=Art+"/WalkShopLetteringAtlas.png";
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
                var g=pair.Value;string path=Art+"/WalkShop_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="WalkShop_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Walk shop batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
