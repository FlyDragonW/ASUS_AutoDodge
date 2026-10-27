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
    // See Sources/cinema-arcade-2025.json and original TVBS2025-01-28 reportage.
    // Only the shared arcade is attributed; interior tenants remain unnamed.
    public static class CinemaArcadeSetup
    {
        public const string RootName = "Cinema arcade • January 2025";
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

        static float width;
        static void Materials()
        {
            M("Column","#514844",.10f,.23f);M("Joint","#282924",0,.14f);
            M("Floor","#858681",0,.20f);M("Inside","#BCB8AB",0,.20f);
            M("Wall","#8B8981",0,.16f);M("Purple","#514059",0,.24f);
            M("Frame","#252927",.13f,.33f);M("Steel","#A5A89E",.43f,.37f);
            M("Paper","#DEDACB",0,.20f);M("PosterGold","#B6A172",0,.19f);
            M("PosterBlue","#57708B",0,.19f);M("PosterRed","#AF5B46",0,.19f);
            M("PosterPurple","#71578C",0,.19f);M("Light","#EEE9D4",0,.18f);
            var light=materials["Light"];light.EnableKeyword("_EMISSION");light.SetColor("_EmissionColor",Hex("#AAA491").linear*.4f);
            FloorTexture();
        }
        static void FloorTexture()
        {
            // Filtered albedo seams remain legible nearby without sub-pixel raised strips
            // producing the speckle observed in the oblique arcade views.
            const int size=256;
            var tile=new Texture2D(size,size,TextureFormat.RGBA32,false);
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(x+.5f)/size,v=(y+.5f)/size;
                float seamDistance=Mathf.Min(Mathf.Min(u,1f-u)*.65f,Mathf.Min(v,1f-v)*.58f);
                float blend=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.0021f,.0049f,seamDistance));
                float shade=Mathf.Lerp(.86f,1f,blend);
                pixels[y*size+x]=new Color(shade,shade,shade,1f);
            }
            string path=Art+"/CinemaArcadeFloorTile.png";
            try{tile.SetPixels32(pixels);tile.Apply();File.WriteAllBytes(path,tile.EncodeToPNG());}
            finally{UnityEngine.Object.DestroyImmediate(tile);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var ti=(TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType=TextureImporterType.Default;ti.sRGBTexture=true;ti.alphaIsTransparency=false;
            ti.mipmapEnabled=true;ti.filterMode=FilterMode.Trilinear;ti.anisoLevel=8;
            ti.wrapMode=TextureWrapMode.Repeat;ti.textureCompression=TextureImporterCompression.Uncompressed;
            ti.maxTextureSize=size;ti.SaveAndReimport();
            var floor=materials["Floor"];
            floor.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            floor.SetTextureScale("_BaseMap",new Vector2(1f/.65f,1f/.58f));
            floor.SetTextureOffset("_BaseMap",Vector2.zero);EditorUtility.SetDirty(floor);
        }
        static void Poster(float x,int index)
        {
            // Original abstract layouts retain witnessed frames and dominant colours, without
            // copying actors, movie art or attributing the still-unidentified interior tenants.
            B("Frame",x,2.08f,.385f,.61f,1.15f,.075f);
            string[] inks={"PosterPurple","PosterRed","PosterGold","PosterBlue","PosterGold"};
            B(inks[index%inks.Length],x,2.08f,.428f,.535f,1.071f,.012f);
            for(int row=0;row<3;row++)B("Paper",x,1.69f+row*.065f,.438f,.31f-row*.047f,.018f,.007f);
            for(int i=0;i<4;i++){
                float yy=2.08f+(i%2)*.17f;
                B(i%2==0?"Paper":"Frame",x-.17f+i*.11f,yy,.439f,.055f,.20f+i*.04f,.007f);
            }
            B("Paper",x,2.47f,.438f,.39f,.034f,.007f);
        }
        static void Arcade()
        {
            const float depth=3.25f;
            // Surface y=.110 already clears shared paving y=.095. Floor seams use
            // the filtered tile texture; their former thin meshes caused distant speckle.
            B("Floor",0,.055f,-1.50f,width,.11f,3.40f);
            // Shallow recess only; its backplane is a modelling limit, not a recorded floorplan.
            B("Inside",0,2.05f,-depth,width,4.10f,.10f);
            B("Inside",0,4.075f,-1.46f,width,.13f,3.58f);
            B("Column",0,4.16f,.0f,width,.08f,.68f);
            B("Purple",0,4.80f,.025f,width,1.20f,.38f);
            B("Steel",0,4.218f,.226f,width,.036f,.024f);
            B("Frame",0,5.37f,.223f,width,.06f,.026f);
            L("東南亞  |  SHOWTIME 秀泰影城",0,4.84f,.225f,.52f,"#D8D1DB",width*.87f);
            L("C I N E M A S",-1.9f,4.48f,.225f,.095f,"#D8D1DB",1.27f);
            float[] columns={-width*.466f,-width*.232f,0,width*.232f,width*.466f};
            for(int i=0;i<columns.Length;i++){
                float x=columns[i];
                B("Column",x,2.05f,-.035f,.82f,4.10f,.77f);
                B("Joint",x,.12f,.361f,.82f,.018f,.010f);
                B("Joint",x,3.55f,.361f,.82f,.015f,.010f);
                Poster(x,i);
            }
            // Four unbranded shallow bays show the observed light recesses behind the shared
            // columns. Exact shop divisions, products and entry plaques are unresolved.
            for(int i=0;i<4;i++){
                float x=(columns[i]+columns[i+1])*.5f;
                float bay=columns[i+1]-columns[i]-.91f;
                B("Wall",x-bay*.5f,1.68f,-2.87f,.12f,3.36f,.63f);
                B("Wall",x+bay*.5f,1.68f,-2.87f,.12f,3.36f,.63f);
                B("Paper",x,3.37f,-2.68f,bay,.16f,.22f);
                B("Light",x,3.265f,-2.50f,bay*.70f,.025f,.045f);
                B("Steel",x,.16f,-2.61f,bay,.045f,.07f);
                B("Paper",x+.28f,1.45f,-3.173f,.26f,.54f,.026f);
                Rod("Light",new Vector3(x,4.002f,-.71f),new Vector3(x,4.020f,-.71f),.073f,16);
            }
            // Regular paving seams are interpretive scale cues in FloorTexture,
            // not measured pavement dimensions or raised geometry.
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build cinema arcade in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id=="CA2501").ToArray();
            if(entries.Length!=1)throw new InvalidOperationException("Merge exactly CA2501 cinema arcade reservation before building.");
            var entry=entries[0];
            if(entry.buildingId!="228711106"||entry.edgeIndex!=0)throw new InvalidOperationException("CA2501 must occupy footprint228711106 edge0 middle slice.");
            if(Mathf.Abs(entry.width-19.15f)>.01f)throw new InvalidOperationException("CA2501 agreed width is19.15m.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var edge=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==0&&e.roadId=="316698035");
            var along=(edge.b-edge.a).normalized;
            var normal=new Vector3(along.z,0,-along.x).normalized;
            var expected=edge.a+along*16.025f+normal*.065f;
            if(Vector3.Distance(entry.center,expected)>.03f)throw new InvalidOperationException("CA2501 centre must preserve edge range6.45..25.60m.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January2025 cinema arcade");
            var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/CinemaArcade/CA2501";Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();width=entry.width;Arcade();BakeLettering();
            var vertices=geometry.Values.SelectMany(g=>g.vertices).ToArray();
            if(vertices.Any(v=>Mathf.Abs(v.x)>width*.5f+.025f||v.y>5.425f||v.z>.46f||v.z< -3.31f))throw new InvalidOperationException("CA2501 geometry exceeds the agreed visible slice/depth/height.");
            if(vertices.Max(v=>v.y)>Mathf.Max(entry.height,entry.visualHeight)+.025f)throw new InvalidOperationException("CA2501 exceeds reservation height.");
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
            string path=Art+"/CinemaArcadeLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/CinemaArcade_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="CinemaArcade_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Cinema arcade batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
