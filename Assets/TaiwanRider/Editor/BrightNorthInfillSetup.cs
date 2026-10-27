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
    // See Sources/bright-north-infill-2025.json and January video03:13/03:16.
    // No tenant identity, pharmacy name or round-sign logo is established.
    public static class BrightNorthInfillSetup
    {
        public const string RootName = "Bright anonymous north entry • January 2025";
        static string Art;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Geometry> geometry = new Dictionary<string, Geometry>();

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
        [Serializable] sealed class Survey { public Building[] buildings; }
        [Serializable] sealed class Building { public string id; public Edge[] roadFrontEdges; }
        [Serializable] sealed class Edge { public string roadId; public int edgeIndex; public Vector3 a,b,outward; public float lengthMetres; }
        static void B(string key,float x,float y,float z,float w,float h,float d) => Box(key,new Vector3(x,y,z),new Vector3(w,h,d));

        static void Materials()
        {
            M("Cream","#DBD6BF",0,.24f);M("White","#E6E3D5",0,.24f);
            M("Gold","#C4A353",0,.25f);M("Inside","#CECABD",0,.20f);
            M("Floor","#AAA99B",0,.17f);M("Frame","#AEAFA3",.40f,.37f);
            M("Dark","#343936",.18f,.24f);M("Notice","#D6BE64",0,.20f);
            M("NoticeInk","#625E47",0,.18f);M("Light","#F0EDD7",0,.20f);
            var light=materials["Light"];light.EnableKeyword("_EMISSION");light.SetColor("_EmissionColor",Hex("#CDC8AD").linear*.35f);
            var glass=M("Glass","#BCC7BF",.08f,.65f);
            glass.SetColor("_BaseColor",new Color(.72f,.78f,.74f,.25f));glass.SetFloat("_Surface",1f);
            glass.SetFloat("_Blend",0f);glass.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            glass.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);glass.SetFloat("_ZWrite",0f);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");glass.renderQueue=3000;
            glass.SetOverrideTag("RenderType","Transparent");EditorUtility.SetDirty(glass);
        }
        static void Facade()
        {
            // Local +X is north. The bright bay is separate from the metal door to its south.
            const float bayWidth=3.70f,bayCentre=.37f;
            B("Floor",bayCentre,.15f,-.86f,bayWidth,.10f,1.70f);
            B("Inside",bayCentre,1.71f,-1.68f,bayWidth,3.10f,.10f);
            B("Cream",bayCentre,3.14f,-.77f,bayWidth,.14f,1.98f);
            foreach(float x in new[]{-1.425f,2.165f})B("Cream",x,1.76f,-.79f,.11f,3.32f,1.90f);
            B("Cream",0,3.49f,-.055f,4.44f,.32f,.20f);
            B("White",bayCentre,3.26f,.055f,bayWidth,.23f,.84f);
            for(int i=0;i<5;i++)Rod("Light",new Vector3(-1.08f+i*.69f,3.126f,.12f),new Vector3(-1.08f+i*.69f,3.143f,.12f),.057f,16);
            // Warm gold solid side and white portal are visible. No inferred shelving/merchandise.
            B("Gold",1.68f,1.66f,-.042f,1.04f,2.92f,.13f);
            B("White",-1.215f,1.65f,-.075f,.42f,2.91f,.15f);
            B("White",.07f,2.99f,-.073f,2.28f,.16f,.16f);
            B("Glass",.075f,1.66f,-.101f,2.15f,2.59f,.026f);
            foreach(float x in new[]{-1f,.075f,1.15f})B("Frame",x,1.66f,-.071f,.033f,2.65f,.058f);
            foreach(float y in new[]{.335f,2.985f})B("Frame",.075f,y,-.07f,2.18f,.037f,.06f);
            foreach(float x in new[]{-.022f,.172f})B("Frame",x,1.32f,-.012f,.018f,.36f,.055f);
            B("Dark",bayCentre,.17f,.022f,bayWidth,.14f,.39f);
            B("Cream",bayCentre,.252f,-.085f,bayWidth,.025f,.60f);
            // Two small yellow notices, unreadable in the source. Marks are neutral abstractions.
            for(int i=0;i<2;i++){
                float x=i==0?-.66f:-1.205f;
                B("Notice",x,1.37f,i==0?-.079f:.008f,i==0?.32f:.28f,.29f,.012f);
                for(int j=0;j<3;j++)B("NoticeInk",x,1.43f-j*.065f,i==0?-.069f:.018f,i==0?.235f:.21f,.019f,.005f);
            }
            B("White",.29f,1.21f,-1.606f,.95f,1.54f,.025f);
            // Narrow dark metal doorway between this bay and NI2501 is not a shop identity.
            B("Dark",-1.91f,1.66f,-.078f,.62f,3.12f,.14f);
            foreach(float x in new[]{-2.19f,-1.63f})B("Frame",x,1.58f,.003f,.035f,2.93f,.055f);
            B("Frame",-1.91f,3.03f,.003f,.59f,.04f,.055f);
            for(int i=0;i<7;i++)B("Frame",-1.91f,.48f+i*.33f,.006f,.42f,.041f,.043f);
            B("Frame",-1.692f,1.39f,.035f,.024f,.24f,.045f);
            // White projecting circular lamp/sign has no reliably readable logo.
            B("Dark",-1.44f,2.69f,.20f,.055f,.055f,.55f);
            Rod("Cream",new Vector3(-1.479f,2.64f,.60f),new Vector3(-1.401f,2.64f,.60f),.255f,32);
            Rod("Light",new Vector3(-1.486f,2.64f,.60f),new Vector3(-1.394f,2.64f,.60f),.229f,32);
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build bright north frontage in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id=="BN2501").ToArray();
            if(entries.Length!=1)throw new InvalidOperationException("Merge exactly BN2501 before building.");
            var entry=entries[0];
            if(entry.buildingId!="707821174"||entry.edgeIndex!=0||Mathf.Abs(entry.width-4.44f)>.01f)throw new InvalidOperationException("BN2501 requires agreed707821174 north edge slice.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var edge=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==0&&e.roadId=="316698035");
            var along=(edge.b-edge.a).normalized;var normal=new Vector3(along.z,0,-along.x).normalized;
            if(Vector3.Distance(entry.center,edge.a+along*2.34f)>.02f)throw new InvalidOperationException("BN2501 must remain in edge interval.12..4.56m.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build anonymous January2025 north entry");
            var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/BrightNorthInfill/BN2501";Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();Materials();Facade();
            var vertices=geometry.Values.SelectMany(g=>g.vertices).ToArray();
            if(vertices.Any(v=>Mathf.Abs(v.x)>2.222f||v.y<0f||v.y>3.652f||v.z>.857f||v.z< -1.772f))throw new InvalidOperationException("BN2501 exceeds agreed bounds.");
            if(vertices.Max(v=>v.y)>Mathf.Max(entry.height,entry.visualHeight)+.01f)throw new InvalidOperationException("BN2501 exceeds reservation height.");
            SaveMeshes(go.transform);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);return root;
        }
        static void SaveMeshes(Transform root)
        {
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/BrightNorthInfill_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="BrightNorthInfill_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Bright north batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
