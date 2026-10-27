using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TaiwanRider.Editor
{
    public static class ScooterHeroSetup
    {
        const string ModelPath="Assets/TaiwanRider/Art/Models/Scooter125_Hero.fbx";
        const string Materials="Assets/TaiwanRider/Materials/Hero125";
        const string HeroName="Player scooter • detailed 125";
        const string MaterialSource="Sources/Blender/Scooter125_Hero-materials.json";
        [Serializable] sealed class AuthoredMaterial
        {
            public string name;
            public float[] color;
            public float metallic,roughness;
        }
        [Serializable] sealed class AuthoredMaterials
        {
            public string colorEncoding;
            public AuthoredMaterial[] materials;
        }
        static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        static string Clean(string name)=>name.Replace(" (Instance)","").Split('.')[0];

        static Dictionary<string,AuthoredMaterial> ReadMaterialSource()
        {
            if(!File.Exists(MaterialSource))throw new FileNotFoundException("Missing authored hero material values",MaterialSource);
            var source=JsonUtility.FromJson<AuthoredMaterials>(File.ReadAllText(MaterialSource));
            if(source==null || source.colorEncoding!="linear" || source.materials==null)
                throw new InvalidDataException("Hero material source must contain Blender linear RGBA values.");
            var result=new Dictionary<string,AuthoredMaterial>(StringComparer.Ordinal);
            foreach(var entry in source.materials)
            {
                if(string.IsNullOrEmpty(entry.name) || entry.color==null || entry.color.Length!=4 ||
                    entry.color.Any(value=>float.IsNaN(value)||float.IsInfinity(value)||value<0||value>1) ||
                    float.IsNaN(entry.metallic) || entry.metallic<0 || entry.metallic>1 ||
                    float.IsNaN(entry.roughness) || entry.roughness<0 || entry.roughness>1)
                    throw new InvalidDataException("Invalid authored material: "+entry.name);
                result.Add(entry.name,entry);
            }
            return result;
        }

        [MenuItem("Taiwan Rider/Refinement/Install detailed player scooter")]
        public static string Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before changing authoring assets.");
            var rider=UnityEngine.Object.FindFirstObjectByType<ScooterController>();
            if(!rider || !rider.visualBody)throw new InvalidOperationException("Open GongguanRide first.");
            var authoredMaterials=ReadMaterialSource();
            var importer=AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if(!importer)throw new FileNotFoundException(ModelPath);
            importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
            importer.SaveAndReimport();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if(!Find(source,"FrontWheel") || !Find(source,"RearWheel") || !Find(source,"FrontSteering"))throw new InvalidOperationException("Hero model needs wheel/steering anchors.");
            foreach(var material in source.GetComponentsInChildren<Renderer>(true).SelectMany(renderer=>renderer.sharedMaterials))
                if(!material || !authoredMaterials.ContainsKey(Clean(material.name)))
                    throw new InvalidDataException("Hero FBX material has no authored values: "+(material?material.name:"null"));
            Directory.CreateDirectory(Materials);AssetDatabase.Refresh();
            // Keep the last working mesh as an inactive authoring backup. Physics stays on the rider root.
            foreach(Transform child in rider.visualBody)
            {
                if(child.name==HeroName)continue;
                if(Find(child.gameObject,"FrontWheel") && Find(child.gameObject,"RearWheel"))child.gameObject.SetActive(false);
            }
            var existing=rider.visualBody.Find(HeroName);
            if(existing)UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var hero=(GameObject)PrefabUtility.InstantiatePrefab(source);
            hero.name=HeroName;hero.transform.SetParent(rider.visualBody,false);
            hero.transform.localPosition=Vector3.zero;hero.transform.localRotation=source.transform.localRotation;
            var fw=Find(hero,"FrontWheel");var rw=Find(hero,"RearWheel");
            if(rider.transform.InverseTransformPoint(fw.position).z<rider.transform.InverseTransformPoint(rw.position).z)
                hero.transform.localRotation=Quaternion.Euler(0,180,0)*hero.transform.localRotation;
            foreach(var t in hero.GetComponentsInChildren<Transform>(true))t.gameObject.layer=8;
            var mapped=new Dictionary<string,Material>();
            foreach(var renderer in hero.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>
                {
                    string name=Clean(original.name);
                    if(mapped.TryGetValue(name,out var found))return found;
                    string path=Materials+"/"+name+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
                    var authored=authoredMaterials[name];
                    // Blender Principled values are linear. URP's Color property is
                    // authored in sRGB and converted by Unity for linear rendering.
                    // Keep the source values intact in JSON instead of trusting FBX's
                    // limited material translation or darkening them a second time.
                    Color color=new Color(authored.color[0],authored.color[1],authored.color[2],authored.color[3]).gamma;
                    float metal=authored.metallic;
                    float smooth=1f-authored.roughness;
                    string key=name.ToLowerInvariant();
                    if(key.Contains("paint")||key.Contains("pearl"))smooth=.60f;
                    if(key=="rubber")smooth=.14f;
                    material.SetColor("_BaseColor",color);material.SetFloat("_Metallic",metal);material.SetFloat("_Smoothness",smooth);material.enableInstancing=true;
                    if(key=="instrument" || key.Contains("instrumentdial"))
                    {
                        var dial=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TaiwanRider/Art/Signs/Instrument.png");
                        material.SetTexture("_BaseMap",dial);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",.3f);
                        material.EnableKeyword("_EMISSION");material.SetTexture("_EmissionMap",dial);material.SetColor("_EmissionColor",new Color(.22f,.24f,.21f));
                    }
                    if(key=="plate")
                    {
                        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TaiwanRider/Art/Signs/Plate.png"));material.SetColor("_BaseColor",Color.white);
                    }
                    EditorUtility.SetDirty(material);mapped.Add(name,material);return material;
                }).ToArray();
            }
            rider.frontVisualWheel=fw;rider.rearVisualWheel=rw;rider.frontSteering=Find(hero,"FrontSteering");
            rider.needle=Find(hero,"GaugeNeedle");
            // Fine grilles and scooter trim become sub-pixel at street distances.
            rider.riderCamera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.TemporalAntiAliasing;
            EditorUtility.SetDirty(rider.riderCamera.GetUniversalAdditionalCameraData());
            foreach(string suffix in new[]{"L","R"})
            {
                var surface=Find(hero,"MirrorSurface_"+suffix);
                if(!surface || !surface.GetComponent<Renderer>())throw new InvalidOperationException("Missing mirror "+suffix);
                var cameraTransform=rider.transform.Find("Rear view "+suffix);
                var camera=cameraTransform?cameraTransform.GetComponent<Camera>():new GameObject("Rear view "+suffix).AddComponent<Camera>();
                camera.transform.SetParent(rider.transform,false);camera.transform.position=surface.GetComponent<Renderer>().bounds.center;
                camera.transform.localRotation=Quaternion.Euler(3,suffix=="L"?166:194,0);camera.fieldOfView=50;camera.nearClipPlane=.03f;camera.farClipPlane=65;camera.cullingMask=~(1<<8);camera.enabled=false;
                camera.GetUniversalAdditionalCameraData().renderShadows=false;
                var mirror=surface.GetComponent<MirrorView>()??surface.gameObject.AddComponent<MirrorView>();mirror.mirrorCamera=camera;mirror.surface=surface.GetComponent<Renderer>();
            }
            EditorUtility.SetDirty(rider);PrefabUtility.RecordPrefabInstancePropertyModifications(hero);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(rider.gameObject.scene);
            return "Hero scooter installed; "+hero.GetComponentsInChildren<Renderer>().Length+" renderers; "+mapped.Count+" materials; wheelbase="+Vector3.Distance(fw.position,rw.position).ToString("F3")+"m";
        }
    }
}
