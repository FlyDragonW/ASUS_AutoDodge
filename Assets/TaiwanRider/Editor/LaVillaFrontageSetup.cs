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
    // See Sources/la-villa-frontage-2025.json; January2025 filming is author-reported.
    // Only the readable La Villa frontage and adjacent directional sign are represented; dimensions are estimated.
    public static class LaVillaFrontageSetup
    {
        public const string RootName = "La Villa observed frontage • January 2025";
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

        static void Materials()
        {
            M("Wall","#AAA593",0,.21f);M("Floor","#7C7970",0,.16f);M("Inside","#202723",0,.14f);
            M("Frame","#343933",.23f,.35f);M("Steel","#A0A69D",.48f,.40f);M("Cream","#DDDCCD",0,.20f);
            M("BrushedMetal","#9EA298",.12f,.19f);
            M("Ink","#272C28",0,.18f);M("BarberMark","#242822",0,.23f);
            M("Glass","#28342F",.05f,.58f);M("Paper","#D9D5BC",0,.18f);M("Notice","#979D80",0,.14f);
            M("Red","#842F28",.10f,.32f);M("Light","#E1E2CE",0,.22f);M("DirectionBlue","#345F97",0,.22f);
            materials["Light"].EnableKeyword("_EMISSION");materials["Light"].SetColor("_EmissionColor",Hex("#CACBAA").linear*.20f);
            var tile=M("Tile","#A7947D",0,.19f);
            const int n=128;var texture=new Texture2D(n,n,TextureFormat.RGBA32,true);
            texture.name="LaVillaPierTile";texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=8;
            var pixels=new Color32[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                float border=Mathf.Min(Mathf.Min(x,n-1-x),Mathf.Min(y,n-1-y));
                float line=1f-Mathf.Clamp01((border-.5f)/2.2f);float grain=((x*17+y*31)%11-5)*.0010f;
                pixels[y*n+x]=new Color(.84f-line*.13f+grain,.80f-line*.13f+grain,.74f-line*.12f+grain,1f);
            }
            texture.SetPixels32(pixels);texture.Apply(true,false);string path=Art+"/LaVillaPierTile.asset";
            var old=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(old){EditorUtility.CopySerialized(texture,old);UnityEngine.Object.DestroyImmediate(texture);texture=old;EditorUtility.SetDirty(texture);}else AssetDatabase.CreateAsset(texture,path);
            tile.SetTexture("_BaseMap",texture);tile.SetTextureScale("_BaseMap",new Vector2(1f/.105f,1f/.215f));EditorUtility.SetDirty(tile);
        }
        static void Frame(Action draw,Vector3 origin,Quaternion rotation)
        {
            var counts=geometry.ToDictionary(p=>p.Key,p=>p.Value.vertices.Count);int labelStart=lettering.Count;draw();
            foreach(var batch in geometry){int from=counts.TryGetValue(batch.Key,out var n)?n:0;for(int i=from;i<batch.Value.vertices.Count;i++)batch.Value.vertices[i]=origin+rotation*batch.Value.vertices[i];}
            for(int i=labelStart;i<lettering.Count;i++){lettering[i].position=origin+rotation*lettering[i].position;lettering[i].yaw+=rotation.eulerAngles.y;}
        }
        static void DirectionBlade()
        {
            // Visible TAIYEN arrow directs around a corner; it is not an additional storefront.
            const float x=-1.30f,y=4.53f,z=.70f;
            // Generic facade occupies localZ[-.09,.09]. The mount penetrates it by.025m,
            // with its front exposed at.145m; both arms intersect the mounting strip.
            foreach(float dy in new[]{-.31f,.31f})B("Steel",x,y+dy,.408f,.050f,.05f,.666f);
            B("Steel",x,y,.105f,.10f,.78f,.08f);
            B("DirectionBlue",x,y,z,.116f,.92f,1.22f);
            foreach(float side in new[]{-1f,1f})Frame(()=>{
                B("Cream",0,.070f,0,1.14f,.71f,.016f);
                L("台塩生技",0,.270f,.026f,.18f,"#345F97",1.03f);
                B("DirectionBlue",0,-.010f,.016f,.92f,.23f,.018f);
                L("TAIYEN",0,-.005f,.048f,.15f,"#E0E4DC",.78f);
                B("DirectionBlue",0,-.295f,.016f,1.14f,.22f,.018f);
                L("巷口左轉 ←",0,-.290f,.048f,.14f,"#E0E4DC",1.03f);
            },new Vector3(x+side*.071f,y,z),Quaternion.Euler(0,side*90f,0));
        }
        static void Facade()
        {
            // Local +X is north, +Z faces the street. The 3.4m slice is an estimate.
            B("Floor",0,.165f,-.88f,3.40f,.13f,2.20f);
            B("Inside",0,1.70f,-1.92f,3.40f,3.10f,.10f);
            foreach(float x in new[]{-1.655f,1.655f})B("Wall",x,1.81f,-.85f,.09f,3.32f,2.12f);
            B("Wall",0,3.57f,-.85f,3.40f,.14f,2.12f);
            B("Wall",0,3.70f,0,3.40f,.10f,.18f); // Close up to the3.75m shell-cut boundary.
            B("Frame",0,.220f,.208f,3.40f,.06f,.13f);
            // Rear edgeZ=-.120 stays18mm ahead of the door bottom rail. TopY=.275
            // clears the front frame by25mm; the underside rests on the floor atY=.230.
            B("BrushedMetal",.13f,.2525f,.0665f,2.18f,.045f,.373f);
            // Cream shop fascia and shallow metal canopy, both attached to the portal.
            B("Cream",0,3.28f,.29f,3.40f,.62f,.20f);
            L("L a  V i l l a",0,3.28f,.426f,.34f,"#303731",2.87f);
            B("Steel",0,3.650f,-.002f,3.40f,.10f,.97f);
            B("Frame",0,3.590f,.460f,3.40f,.065f,.047f);
            for(int i=0;i<8;i++)B("Steel",0,3.584f,-.410f+i*.112f,3.30f,.033f,.039f);
            foreach(float x in new[]{-1.25f,1.25f})Rod("Frame",new Vector3(x,3.13f,-.03f),new Vector3(x,3.58f,.38f),.016f,8);
            // Dark recessed double entry and narrow opaque utility return.
            B("Glass",.18f,1.59f,-.22f,2.25f,2.68f,.046f);
            foreach(float x in new[]{-.96f,.18f,1.32f})B("Frame",x,1.59f,-.173f,.045f,2.75f,.07f);
            foreach(float y in new[]{.238f,2.952f})B("Frame",.18f,y,-.173f,2.31f,.041f,.07f);
            foreach(float x in new[]{.071f,.289f}){
                Rod("Steel",new Vector3(x,1.06f,-.10f),new Vector3(x,1.53f,-.10f),.012f,10);
                foreach(float y in new[]{1.06f,1.53f})B("Steel",x,y,-.134f,.027f,.024f,.08f);
            }
            B("Paper",.84f,2.14f,-.161f,.61f,.72f,.016f);
            for(int i=0;i<7;i++)B("Notice",.84f,2.36f-i*.064f,-.145f,i<2?.42f:.48f,.020f,.009f);
            B("Light",.15f,2.75f,-.94f,.54f,.08f,.055f);
            // North cream BARBER'S SELECT direction panel. The earlier bottle reading
            // was wrong: the original03:10 frame shows a black/white striped barber pole.
            B("Frame",1.465f,1.66f,.005f,.47f,2.84f,.28f);
            B("Light",1.465f,1.66f,.167f,.42f,2.75f,.035f);
            B("BarberMark",1.465f,2.48f,.198f,.155f,.38f,.017f);
            B("Paper",1.465f,2.495f,.213f,.118f,.310f,.012f);
            for(int i=0;i<4;i++){
                float yy=2.386f+i*.060f,xx=1.465f,zz=.229f,hw=.059f;
                G("BarberMark").Quad(new Vector3(xx-hw,yy-.035f,zz),new Vector3(xx+hw,yy+.014f,zz),new Vector3(xx+hw,yy+.035f,zz),new Vector3(xx-hw,yy-.014f,zz));
            }
            B("BarberMark",1.465f,2.305f,.211f,.155f,.045f,.027f);
            B("BarberMark",1.465f,2.683f,.211f,.140f,.045f,.027f);
            B("BarberMark",1.465f,2.727f,.198f,.064f,.081f,.017f);
            Label("BARBER'S SELECT",new Vector3(1.465f,1.30f,.211f),.128f,"#373C32",1.85f,0,-90f);
            B("Ink",1.465f,.325f,.201f,.42f,.12f,.027f);
            // South tiled pier and utility box. A possible205 plate is not a confirmed address.
            B("Tile",-1.345f,1.64f,-.008f,.71f,2.92f,.36f);
            B("Ink",-1.325f,1.94f,.222f,.55f,.31f,.18f);
            B("BrushedMetal",-1.325f,2.014f,.319f,.43f,.023f,.016f);
            B("Ink",-1.425f,1.325f,.185f,.43f,.56f,.041f);
            B("Steel",-1.23f,1.35f,.218f,.021f,.11f,.02f);
            B("Paper",-1.335f,2.28f,.194f,.27f,.115f,.016f);
            for(int i=0;i<3;i++)B("Notice",-1.412f+i*.075f,2.28f,.210f,.040f,.057f,.012f);
            // Cylinder, short nozzle, hose and label reproduce the visible extinguisher silhouette.
            Rod("Red",new Vector3(-1.20f,.39f,.292f),new Vector3(-1.20f,.84f,.292f),.086f,16);
            Rod("Steel",new Vector3(-1.20f,.84f,.292f),new Vector3(-1.20f,.91f,.292f),.028f,10);
            B("Red",-1.18f,.94f,.292f,.17f,.025f,.055f);
            B("Paper",-1.20f,.655f,.380f,.093f,.19f,.013f);
            Rod("Ink",new Vector3(-1.26f,.90f,.29f),new Vector3(-1.33f,.74f,.29f),.014f,8);
            Rod("Ink",new Vector3(-1.33f,.74f,.29f),new Vector3(-1.32f,.47f,.29f),.014f,8);
            DirectionBlade();
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build La Villa frontage in Edit mode.");if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id=="LV2501").ToArray();
            if(entries.Length!=1)throw new InvalidOperationException("Merge LV2501 reservation before building.");
            var entry=entries[0];if(entry.buildingId!="580823989"||entry.edgeIndex!=3||Mathf.Abs(entry.width-3.40f)>.01f||Mathf.Abs(entry.height-3.75f)>.01f||Mathf.Abs(entry.visualHeight-5.10f)>.01f)throw new InvalidOperationException("LV2501 requires580823989 edge3 agreed3.4m, shell cut3.75m and visual height5.10m.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var edge=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==3&&e.roadId=="316698035");
            var along=(edge.b-edge.a).normalized;var normal=new Vector3(along.z,0,-along.x).normalized;
            if(Vector3.Distance(entry.center,edge.b-along*9.92f)>.02f)throw new InvalidOperationException("LV2501 must stay in13.048271..16.448271m edge interval.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build observed La Villa frontage");
            var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/LaVillaFrontage/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();Facade();BakeLettering();
            var vertices=geometry.Values.SelectMany(g=>g.vertices).ToArray();
            if(vertices.Any(v=>Mathf.Abs(v.x)>1.701f||v.y<0f||v.y>5.10f||v.z>1.32f||v.z< -1.98f))throw new InvalidOperationException("LV2501 exceeds its agreed slice/depth/height.");
            if(vertices.Max(v=>v.y)>Mathf.Max(entry.height,entry.visualHeight)+.01f)throw new InvalidOperationException("LV2501 exceeds its reservation height.");
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
            string path=Art+"/LaVillaFrontageLetteringAtlas.png";
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
                var g=pair.Value;string path=Art+"/LaVillaFrontage_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="LaVillaFrontage_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("La Villa batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
