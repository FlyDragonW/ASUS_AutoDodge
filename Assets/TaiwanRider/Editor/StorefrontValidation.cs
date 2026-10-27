using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TaiwanRider.Editor
{
    public static class StorefrontValidation
    {
        [Serializable] sealed class Check { public string name,detail; public bool passed; }
        [Serializable] sealed class Report
        {
            public string timeUtc,scene,ridingPhysicsHash;
            public int passed,failed,storefrontGroups,renderers,triangles,mappedGroups,unmappedGroups;
            public List<Check> checks=new List<Check>();
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public static string Save(string expectedPhysicsHash,string path="Validation/AdditionalShops/integration.json")
        {
            var report=new Report{timeUtc=DateTime.UtcNow.ToString("O"),scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,ridingPhysicsHash=SurveyValidation.RidingPhysicsHash()};
            void Check(string name,bool passed,string detail)
            {report.checks.Add(new Check{name=name,passed=passed,detail=detail});if(passed)report.passed++;else report.failed++;}
            var root=GameObject.Find(SurveyStreetscapeSetup.RootName);
            Check("Surveyed street root exists",root!=null,SurveyStreetscapeSetup.RootName);
            Check("Edit mode",!EditorApplication.isPlaying,"Storefronts are saved scene geometry.");
            Check("Riding physics unchanged",report.ridingPhysicsHash==expectedPhysicsHash,report.ridingPhysicsHash);
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages();report.storefrontGroups=entries.Length;
            Check("Storefront dataset has unique ids",entries.Length>0&&entries.Select(e=>e.id).Distinct().Count()==entries.Length,entries.Length+" reservations");
            var buildings=SurveyStreetscapeSetup.LoadFacadeBuildings();
            foreach(var cut in SurveyStreetscapeSetup.LoadVisualCuts())
            {
                var host=buildings.FirstOrDefault(b=>b.id==cut.buildingId);
                bool valid=host!=null&&cut.edgeIndex>=0&&cut.edgeIndex<host.points.Length;
                Check(cut.shopId+" partial visual cut references shop and edge",valid&&entries.Any(e=>e.id==cut.shopId),cut.buildingId+" edge "+cut.edgeIndex);
                if(!valid)continue;
                try{
                    var spans=SurveyStreetscapeSetup.FacadeSpans(host,cut.edgeIndex,entries);
                    bool disjoint=true;for(int i=1;i<spans.Length;i++)if(spans[i].from<spans[i-1].to-.025f)disjoint=false;
                    Check(cut.shopId+" partial visual cut is bounded and disjoint",disjoint,"Original footprint and collision geometry preserved.");
                }catch(Exception e){Check(cut.shopId+" partial visual cut is bounded and disjoint",false,e.Message);}
            }
            var transforms=root?root.GetComponentsInChildren<Transform>(true):Array.Empty<Transform>();
            foreach(var entry in entries)
            {
                var found=transforms.Where(t=>t.name==entry.sceneName).ToArray();
                Check(entry.id+" unique scene group",found.Length==1,entry.sceneName);
                if(found.Length!=1)continue;
                var group=found[0];var renderers=group.GetComponentsInChildren<MeshRenderer>(true);
                report.renderers+=renderers.Length;
                Check(entry.id+" visible active group",group.gameObject.activeInHierarchy&&renderers.Any(r=>r.enabled&&r.gameObject.activeInHierarchy),"At least one enabled renderer in an active hierarchy.");
                Check(entry.id+" placement matches evidence manifest",Vector3.Distance(new Vector3(group.position.x,0,group.position.z),entry.center)<.12f,group.position.ToString("F4"));
                Check(entry.id+" renderable original meshes",renderers.Length>0&&renderers.All(r=>r.GetComponent<MeshFilter>()&&r.GetComponent<MeshFilter>().sharedMesh&&r.GetComponent<MeshFilter>().sharedMesh.vertexCount>0),renderers.Length+" batches");
                Check(entry.id+" visual only",group.GetComponentsInChildren<Collider>(true).Length==0&&group.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==2),"Layer 2, no new radar or vehicle collider.");
                Check(entry.id+" materials resolved",renderers.All(r=>r.sharedMaterials.Length>0&&r.sharedMaterials.All(m=>m&&m.shader&&m.shader.name!="Hidden/InternalErrorShader")),"All mesh materials and shaders assigned.");
                bool finite=true;float maxY=float.NegativeInfinity,minY=float.PositiveInfinity;
                foreach(var renderer in renderers)
                {
                    var filter=renderer.GetComponent<MeshFilter>();var mesh=filter?filter.sharedMesh:null;if(!mesh){finite=false;continue;}
                    report.triangles+=mesh.triangles.Length/3;
                    finite&=mesh.vertices.All(v=>Finite(v.x)&&Finite(v.y)&&Finite(v.z));
                    maxY=Mathf.Max(maxY,renderer.bounds.max.y);minY=Mathf.Min(minY,renderer.bounds.min.y);
                }
                float visualLimit=entry.visualHeight>0?entry.visualHeight:entry.height;
                Check(entry.id+" finite geometry and reserved height",finite&&maxY<=visualLimit+.08f&&minY>=-.25f,$"world y {minY:F3}..{maxY:F3}; visual limit {visualLimit:F3}; wall clearance {entry.height:F3}");
                if(string.IsNullOrEmpty(entry.buildingId))
                {
                    report.unmappedGroups++;
                    Check(entry.id+" OSM gap explicitly identified",!string.IsNullOrEmpty(entry.sourceFile),"Photo/POI estimated frontage in an unmapped footprint gap; no invented OSM id.");
                    continue;
                }
                report.mappedGroups++;
                var building=buildings.FirstOrDefault(b=>b.id==entry.buildingId);
                Check(entry.id+" source footprint exists",building!=null,entry.buildingId);if(building==null)continue;
                bool validEdge=entry.edgeIndex>=0&&entry.edgeIndex<building.points.Length;
                Check(entry.id+" source edge exists",validEdge,entry.edgeIndex.ToString());if(!validEdge)continue;
                try
                {
                    var spans=SurveyStreetscapeSetup.FacadeSpans(building,entry.edgeIndex,entries);
                    bool disjoint=true;for(int i=1;i<spans.Length;i++)if(spans[i].from<spans[i-1].to-.025f)disjoint=false;
                    Check(entry.id+" facade reservations disjoint",disjoint,"Includes existing dated crops on this edge.");
                }
                catch(Exception e){Check(entry.id+" reservation lies inside footprint edge",false,e.Message);}
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            return $"{report.passed} passed, {report.failed} failed; {report.storefrontGroups} storefront groups, {report.triangles} triangles. {Path.GetFullPath(path)}";
        }
    }
}
