using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TaiwanRider.Editor
{
    public static class SiteMatchValidation
    {
        [Serializable] sealed class Check {public string name,detail;public bool passed;}
        [Serializable] sealed class Report
        {public string timeUtc,scene,physicsHash;public int passed,failed;public List<Check> checks=new List<Check>();}
        static string PathOf(Transform t)=>t.parent?PathOf(t.parent)+"/"+t.name:t.name;
        static IEnumerable<T> All<T>() where T:Component=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));
        static void Test(Report r,string name,bool success,string detail)
        {r.checks.Add(new Check{name=name,passed=success,detail=detail});if(success)r.passed++;else r.failed++;}
        public static string PhysicsHash()
        {
            // Compare the actual authoring physics components across this visual-only pass.
            var rows=All<Collider>().Select(c=>PathOf(c.transform)+"|"+c.GetType().Name+"|"+c.gameObject.layer+"|"+c.gameObject.activeInHierarchy+"|"+c.transform.position.ToString("F6")+"|"+c.transform.rotation.ToString("F6")+"|"+c.transform.lossyScale.ToString("F6")+"|"+EditorJsonUtility.ToJson(c));
            string input=string.Join("\n",rows.OrderBy(v=>v,StringComparer.Ordinal));
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-","");
        }
        public static string Save(string expectedPhysicsHash,string path="Validation/SiteMatch/integration.json")
        {
            var scene=SceneManager.GetActiveScene();var r=new Report{timeUtc=DateTime.UtcNow.ToString("O"),scene=scene.path,physicsHash=PhysicsHash()};
            Test(r,"Original collision geometry unchanged",r.physicsHash==expectedPhysicsHash,r.physicsHash);
            var roots=scene.GetRootGameObjects();var root=roots.FirstOrDefault(g=>g.name==SiteMatchSetup.RootName);
            Test(r,"One installed site match",roots.Count(g=>g.name==SiteMatchSetup.RootName)==1,"Photographic study is distinct from unverified background");
            Test(r,"Official business pin used",root&&Vector3.Distance(root.transform.position,SiteMatchSetup.ReferencePin)<.002f,SiteMatchSetup.ReferencePin.ToString("F3"));
            var facade=root?root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==SiteFacadeSetup.RootName):null;
            Test(r,"Photographic facade present",facade,"2025 article; capture date unknown");
            if(facade)
            {
                Test(r,"Frontage faces the main street",Vector3.Dot(facade.forward,Vector3.right)>.999f,"Local +Z faces world +X");
                Test(r,"Photographic proportions unscaled",Vector3.Distance(facade.lossyScale,Vector3.one)<.001f,"11 m width / 6.85 m visible crop are estimates");
            }
            if(root)
            {
                var renderers=root.GetComponentsInChildren<Renderer>();
                Test(r,"Site materials resolved",renderers.Length>0&&renderers.All(v=>v.sharedMaterials.All(m=>m&&m.shader&&m.shader.name!="Hidden/InternalErrorShader")),renderers.Length+" renderers");
                Test(r,"Site adds no colliders",root.GetComponentsInChildren<Collider>(true).Length==0,"Original collision proxies remain authoritative");
                Test(r,"Site excluded from radar",root.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==2),"Ignore Raycast layer 2");
            }
            var lane=roots.FirstOrDefault(g=>g.name==SiteLaneSetup.RootName);var oldLane=All<Transform>().FirstOrDefault(t=>t.name=="Lane 90"&&t.parent&&t.parent.name=="Street geometry");
            Test(r,"Straight prototype visual hidden",oldLane&&oldLane.GetComponent<MeshRenderer>()&&!oldLane.GetComponent<MeshRenderer>().enabled,"Original Lane 90 collider retained");
            Test(r,"Curved Lane 90 present",lane&&lane.GetComponent<MeshFilter>()&&lane.GetComponent<MeshFilter>().sharedMesh,"Cached OSM centreline");
            if(lane)
            {
                var mesh=lane.GetComponent<MeshFilter>().sharedMesh;
                Test(r,"Curved lane faces upward",mesh.normals.Length>0&&mesh.normals.All(n=>n.y>.999f),"No inverted asphalt faces");
                Test(r,"Curved lane reaches cached OSM endpoint",Mathf.Abs(mesh.bounds.max.x-77.5f)<2f&&mesh.bounds.min.z< -12f,mesh.bounds.ToString());
                Test(r,"Distant road is visual only",lane.GetComponentsInChildren<Collider>(true).Length==0,"Existing playable near segment unchanged");
            }
            Test(r,"Invented alley termination removed",!All<Renderer>().Any(v=>v.name=="Buildings_and_pavement_BackdropAsphalt"&&v.enabled&&v.gameObject.activeInHierarchy),"No fictional backdrop crosses OSM lane");
            var context=All<Transform>().FirstOrDefault(t=>t.name==IntersectionDetailSetup.ContextRootName);
            Test(r,"Unverified context identified separately",context&&context.gameObject.activeInHierarchy,"Procedural background is not claimed as surveyed architecture");
            Test(r,"Context excluded from physics and radar",context&&context.GetComponentsInChildren<Collider>(true).Length==0&&context.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==2),"Visual context has no physics effect");
            foreach(string name in new[]{"Building_-1_14","Building_-1_15"})
            {var original=All<Transform>().FirstOrDefault(t=>t.name==name);Test(r,"Old facade hidden: "+name,original&&!original.gameObject.activeInHierarchy,"Inactive backup retained");}
            string json=JsonUtility.ToJson(r,true);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));File.WriteAllText(path,json);return json;
        }
    }
}
