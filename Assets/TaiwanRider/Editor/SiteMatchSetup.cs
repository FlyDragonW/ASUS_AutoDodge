using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TaiwanRider.Editor
{
    /// <summary>Integrates the evidenced storefront crop into the existing prototype.
    /// A business pin is an approximate placement anchor, not a surveyed facade corner.</summary>
    public static class SiteMatchSetup
    {
        public const string RootName="Gongguan 2025 site match";
        const string Art="Assets/TaiwanRider/Art/SiteMatch";
        public static Vector3 ReferencePin
        {
            get
            {
                double east=(121.5343037-121.5344569)*111320.0*Math.Cos(25.0135214*Math.PI/180.0);
                double north=(25.0135104-25.0135214)*111320.0;
                return new Vector3((float)(east*.62747+north*.77865),0,(float)(east*-.77865+north*.62747));
            }
        }
        [MenuItem("Taiwan Rider/Site Match/Apply 2025 photo frontage and Lane 90")]
        public static void Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!="Assets/TaiwanRider/Scenes/GongguanRide.unity")
                throw new InvalidOperationException("Open GongguanRide outside Play mode.");
            // Rebuild combined geometry with the two overlapping invented shopfronts omitted.
            IntersectionDetailSetup.Apply(-1,3.2f,19f,includeAlleyBackdrop:false,curveContextToLane:true);
            StreetLifeSetup.Apply(-1,3.2f,19f);
            SiteLaneSetup.Apply();
            var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(old)Undo.DestroyObjectImmediate(old);
            var root=new GameObject(RootName){layer=2};root.transform.position=ReferencePin;
            Undo.RegisterCreatedObjectUndo(root,"Install 2025 photographed storefront study");
            SiteFacadeSetup.Build(root.transform,ReferencePin,Quaternion.Euler(0,90,0));
            AddSupportShell(root.transform);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("2025 photo storefront installed at the official business pin approximation. Width, building depth, height, road width and surrounding blocks are not surveyed. Scene remains unsaved for review.");
        }
        static void AddSupportShell(Transform parent)
        {
            // These recessed closure faces prevent a view through the photo crop. They are
            // explicitly separate from the evidenced geometry and contain no invented signage.
            Directory.CreateDirectory(Art);
            string path=Art+"/UnverifiedSupport.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.name="Unverified neutral building closure";
            material.SetColor("_BaseColor",new Color(.45f,.47f,.445f));material.SetFloat("_Smoothness",.13f);EditorUtility.SetDirty(material);
            var shell=GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name="Unverified support shell • depth and roof height are estimates";shell.layer=2;
            UnityEngine.Object.DestroyImmediate(shell.GetComponent<Collider>());
            shell.transform.SetParent(parent,false);shell.transform.localPosition=new Vector3(-4.9f,3.43f,0);
            shell.transform.localScale=new Vector3(6.4f,6.86f,15.65f);
            shell.GetComponent<MeshRenderer>().sharedMaterial=material;
            GameObjectUtility.SetStaticEditorFlags(shell,StaticEditorFlags.BatchingStatic);
        }
    }
}
