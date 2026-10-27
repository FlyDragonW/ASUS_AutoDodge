using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TaiwanRider.Editor
{
    public static class TrafficFleetSetup
    {
        const string ModelFolder="Assets/TaiwanRider/Art/Models/";
        const string MaterialFolder="Assets/TaiwanRider/Materials/TrafficFleet";
        const string Backup="Original vehicle (inactive backup)";
        [Serializable] sealed class MaterialSpec {public string name;public float[] color;public float metallic,roughness;}
        [Serializable] sealed class MaterialDocument {public string colorEncoding;public MaterialSpec[] materials;}
        static Dictionary<string,Material> materials;
        static GameObject[] scooters,cars;
        static readonly string[] PaintColors={"#2D343C","#E5E4DD","#66747D","#213D55","#692F38","#A5A7A4"};
        static string Clean(string name)=>name.Replace(" (Instance)","").Split('.')[0];
        static Transform Find(Transform parent,string name)=>parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);

        [MenuItem("Taiwan Rider/Refinement/Install detailed traffic fleet")]
        public static string Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before installing the fleet.");
            var parked=GameObject.Find("Parked scooters");var route=UnityEngine.Object.FindFirstObjectByType<RoadRoute>();
            if(!parked || !route)throw new InvalidOperationException("Open GongguanRide first.");
            Directory.CreateDirectory(MaterialFolder);AssetDatabase.Refresh();materials=new Dictionary<string,Material>();
            ReadMaterials("Sources/Blender/Scooter125_Hero-materials.json",true);
            ReadMaterials("Sources/Blender/CompactSedan_Detailed-materials.json",false);
            scooters=Import("Scooter125_Fleet");cars=Import("CompactSedan_Detailed");
            var parkedScooters=parked.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Parked scooter ")).OrderBy(t=>int.Parse(t.name.Substring(15))).ToArray();
            if(parkedScooters.Length!=42)throw new InvalidOperationException("Expected 42 parked scooters, found "+parkedScooters.Length);
            float[][] clusters={new float[]{-92,-77,-49,-27,24,57,78},new float[]{-91,-68,-47,-25,26,51,84}};
            for(int i=0;i<parkedScooters.Length;i++)
            {
                int bank=i/21,slot=i%21,side=bank==0?-1:1;
                float z=clusters[bank][slot/3]+(slot%3-1)*1.55f;
                var root=parkedScooters[i];root.position=route.Position(z,side*(5.34f+(slot%3)*.025f),.02f);
                root.rotation=route.Heading(z)*Quaternion.Euler(0,-side*(56+(slot%3)*3),0);
                Replace(root,false,i,false);
            }
            int carIndex=0;
            foreach(var root in parked.transform.Cast<Transform>().Where(t=>t.name=="Parked sedan"))Replace(root,true,++carIndex,false);
            int actors=0;
            foreach(var actor in UnityEngine.Object.FindObjectsByType<HazardActor>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                bool car=actor.kind==ScenarioKind.CarCutIn;Replace(actor.transform,car,car?2:4,true);actor.AlignModelForward();actors++;
            }
            ParkingPaint(route,clusters);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(parked.scene);
            return $"Installed detailed fleet: {parkedScooters.Length} parked scooters, {carIndex} parked cars, {actors} moving hazards; three LODs each.";
        }
        static void ParkingPaint(RoadRoute route,float[][] clusters)
        {
            const string name="Refined parking markings";
            var old=GameObject.Find(name);if(old)UnityEngine.Object.DestroyImmediate(old);
            // Parking boxes follow the actual parked groups. Keep red curbs at junctions.
            foreach(var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(t.name=="Parking bay" || t.name.StartsWith("Edge line "))t.gameObject.SetActive(false);
                if(t.name!="Red curb marking")continue;
                float z=t.position.z;int bank=t.position.x<route.CenterX(z)?0:1;
                bool inBay=clusters[bank].Any(c=>Mathf.Abs(z-c)<3.85f) || bank==0 && new[]{-64f,43f,89f}.Any(c=>Mathf.Abs(z-c)<4.1f);
                t.gameObject.SetActive(!inBay);
            }
            var vertices=new List<Vector3>();var triangles=new List<int>();
            void Stripe(Vector3 a,Vector3 b,float width)
            {
                Vector3 side=Vector3.Cross(Vector3.up,(b-a).normalized)*width*.5f;int n=vertices.Count;
                vertices.AddRange(new[]{a-side,a+side,b+side,b-side});triangles.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
            }
            for(int bank=0;bank<2;bank++)
            {
                int side=bank==0?-1:1;
                for(float z=-116;z<116;z+=2)Stripe(route.Position(z,side*4.12f,.016f),route.Position(z+2,side*4.12f,.016f),.08f);
                foreach(float center in clusters[bank])foreach(float dz in new[]{-2.325f,-.775f,.775f,2.325f})
                    Stripe(route.Position(center+dz,side*4.12f,.017f),route.Position(center+dz,side*6.05f,.017f),.065f);
            }
            foreach(float center in new[]{-64f,43f,89f})foreach(float dz in new[]{-2.65f,2.65f})
                Stripe(route.Position(center+dz,-4.12f,.017f),route.Position(center+dz,-6.05f,.017f),.08f);
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=MaterialFolder+"/ParkingPaint.asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
            var root=new GameObject(name){layer=2};root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/TaiwanRider/Materials/RoadWhite.mat");
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        static void ReadMaterials(string path,bool hero)
        {
            var doc=JsonUtility.FromJson<MaterialDocument>(File.ReadAllText(path));
            if(doc.colorEncoding!="linear")throw new InvalidDataException(path+": expected linear material colors");
            foreach(var spec in doc.materials)
            {
                string target=hero?"Assets/TaiwanRider/Materials/Hero125/"+spec.name+".mat":MaterialFolder+"/"+spec.name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(target);
                if(hero && !mat)throw new FileNotFoundException(target);
                if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,target);}
                if(!hero)
                {
                    mat.SetColor("_BaseColor",new Color(spec.color[0],spec.color[1],spec.color[2],spec.color[3]).gamma);
                    mat.SetFloat("_Metallic",spec.metallic);mat.SetFloat("_Smoothness",1-spec.roughness);
                    mat.enableInstancing=true;EditorUtility.SetDirty(mat);
                }
                materials.Add(spec.name,mat);
            }
            string paintName=hero?"Hero_Pearl":"SedanBody";
            for(int i=0;i<PaintColors.Length;i++)
            {
                string key=paintName+"_"+i,target=MaterialFolder+"/"+key+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(target);
                if(!mat){mat=new Material(materials[paintName]);AssetDatabase.CreateAsset(mat,target);}
                ColorUtility.TryParseHtmlString(PaintColors[i],out var color);mat.SetColor("_BaseColor",color);
                mat.SetFloat("_Smoothness",.63f);mat.enableInstancing=true;EditorUtility.SetDirty(mat);materials.Add(key,mat);
            }
        }
        static GameObject[] Import(string stem)
        {
            var result=new GameObject[3];
            for(int i=0;i<3;i++)
            {
                string path=ModelFolder+stem+"_LOD"+i+".fbx";
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                if(!importer)throw new FileNotFoundException(path);
                importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
                importer.SaveAndReimport();result[i]=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return result;
        }
        static void Replace(Transform root,bool car,int paint,bool moving)
        {
            string name=car?"Sedan":"Scooter125_LOD";
            var old=root.Find(name);
            if(old)
            {
                if(old.GetComponent<LODGroup>())UnityEngine.Object.DestroyImmediate(old.gameObject);
                else {old.name=Backup;old.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject);}
            }
            var previousGroup=root.GetComponent<LODGroup>();if(previousGroup)UnityEngine.Object.DestroyImmediate(previousGroup);
            var container=new GameObject(name);container.transform.SetParent(root,false);
            var group=container.AddComponent<LODGroup>();var lods=new LOD[3];var wheels=new List<Transform>();
            for(int level=0;level<3;level++)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab((car?cars:scooters)[level]);
                instance.name="Detailed LOD"+level;instance.transform.SetParent(container.transform,false);
                foreach(var part in instance.GetComponentsInChildren<Transform>(true))
                {part.gameObject.layer=root.gameObject.layer;PrefabUtility.RecordPrefabInstancePropertyModifications(part.gameObject);}
                var front=Find(instance.transform,car?"Front grille":"FrontWheel");
                var rear=Find(instance.transform,car?"Rear plate":"RearWheel");
                if(!front || !rear)throw new InvalidDataException("Missing forward anchors on "+instance.name);
                Vector3 forward=container.transform.InverseTransformDirection(front.position-rear.position);
                instance.transform.localRotation=Quaternion.AngleAxis(-Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg,Vector3.up)*instance.transform.localRotation;
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>
                    {
                        string key=Clean(original.name);
                        if(key==(car?"SedanBody":"Hero_Pearl"))key+="_"+(paint%PaintColors.Length);
                        if(!materials.TryGetValue(key,out var mapped))throw new InvalidDataException("Unmapped fleet material: "+key);
                        return mapped;
                    }).ToArray();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                wheels.AddRange(instance.GetComponentsInChildren<Transform>(true).Where(t=>car?t.name.StartsWith("Wheel_") && t.name.Count(c=>c=='_')==1:t.name=="FrontWheel"||t.name=="RearWheel"));
                lods[level]=new LOD(level==0?.19f:level==1?.075f:.007f,instance.GetComponentsInChildren<Renderer>(true));
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            }
            group.SetLODs(lods);group.fadeMode=LODFadeMode.None;group.RecalculateBounds();
            if(moving)
            {
                var motion=root.GetComponent<TrafficWheelMotion>()??root.gameObject.AddComponent<TrafficWheelMotion>();
                motion.wheels=wheels.ToArray();motion.radius=car?.318f:.217f;EditorUtility.SetDirty(motion);
            }
        }
    }
}
