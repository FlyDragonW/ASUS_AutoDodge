using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace TaiwanRider.Editor
{
    public static class TrafficFleetValidation
    {
        [Serializable] sealed class Check {public string name,detail;public bool passed;}
        [Serializable] sealed class Report {public string capturedAt;public Check[] checks;}
        public static string Save(string path="Validation/StreetFleet/integration.json")
        {
            var checks=new List<Check>();
            void Test(string name,bool passed,string detail)=>checks.Add(new Check{name=name,passed=passed,detail=detail});
            var parked=GameObject.Find("Parked scooters");
            var actors=UnityEngine.Object.FindObjectsByType<HazardActor>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var roots=parked.transform.Cast<Transform>().Concat(actors.Select(a=>a.transform)).ToArray();
            Test("Complete roadside and hazard fleet",parked.transform.childCount==45 && actors.Length==2,"45 parked + 2 moving vehicles");
            foreach(var root in roots)
            {
                var groups=root.GetComponentsInChildren<LODGroup>(true).Where(g=>g.gameObject.activeSelf).ToArray();
                Test(root.name+" has three distinct detail levels",groups.Length==1 && groups[0].GetLODs().Length==3,"LOD groups="+groups.Length);
                if(groups.Length!=1)continue;
                var lods=groups[0].GetLODs();
                Test(root.name+" uses independent LOD assets",lods.SelectMany(l=>l.renderers).Distinct().Count()==lods.Sum(l=>l.renderers.Length) && lods.Select((l,i)=>l.renderers.All(r=>AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh).EndsWith("_LOD"+i+".fbx"))).All(ok=>ok),"Separate renderers and FBX files");
                var tri=lods.Select(l=>l.renderers.Sum(r=>{var mf=r.GetComponent<MeshFilter>();return mf && mf.sharedMesh?Enumerable.Range(0,mf.sharedMesh.subMeshCount).Sum(s=>(long)mf.sharedMesh.GetIndexCount(s)/3):0;})).ToArray();
                Test(root.name+" reduces geometry with distance",tri.Length==3 && tri[0]>tri[1]*2 && tri[1]>tri[2]*2,string.Join(" / ",tri));
                Test(root.name+" all materials resolve",lods.All(l=>l.renderers.Length>0 && l.renderers.All(r=>r && r.sharedMaterials.All(m=>m && m.shader && m.shader.name!="Hidden/InternalErrorShader"))),"All LOD renderers");
                var box=root.GetComponent<BoxCollider>();bool car=root.Find("Sedan"),moving=root.GetComponent<HazardActor>();
                Vector3 expectedSize=car?new Vector3(1.78f,moving?1.42f:1.45f,4.52f):new Vector3(.62f,1.05f,moving?1.79f:1.73f);
                Vector3 expectedCenter=new Vector3(0,car?(moving?.74f:.73f):(moving?.59f:.57f),0);
                Test(root.name+" preserves collision",box && box.enabled && !box.isTrigger && Vector3.Distance(box.size,expectedSize)<.001f && Vector3.Distance(box.center,expectedCenter)<.001f && root.GetComponentsInChildren<Collider>(true).Count(c=>c.enabled && c.transform!=root && !c.transform.IsChildOf(root.Find("Original vehicle (inactive backup)")))==0,"Original enabled collision box, dimensions and center; no mesh collider");
                var bounds=new Bounds();bool first=true;
                foreach(var renderer in lods[0].renderers)
                {
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                    for(int corner=0;corner<8;corner++)
                    {
                        var p=mesh.center+Vector3.Scale(mesh.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                        p=root.InverseTransformPoint(renderer.transform.TransformPoint(p));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                    }
                }
                Test(root.name+" imported at vehicle scale",car?bounds.size.x>1.75f && bounds.size.x<2.15f && bounds.size.y>1.4f && bounds.size.y<1.65f && bounds.size.z>4.4f && bounds.size.z<4.7f:bounds.size.x>.5f && bounds.size.x<1.1f && bounds.size.y>1f && bounds.size.y<1.6f && bounds.size.z>1.6f && bounds.size.z<2.3f,bounds.size.ToString("F3"));
                Test(root.name+" no fleet mirror cameras",groups[0].GetComponentsInChildren<Camera>(true).Length==0 && groups[0].GetComponentsInChildren<MirrorView>(true).Length==0,"Shared reflective surfaces");
            }
            var overlaps=new List<string>();var boxes=parked.GetComponentsInChildren<BoxCollider>();
            for(int a=0;a<boxes.Length;a++)for(int b=a+1;b<boxes.Length;b++)
            {
                if(Physics.ComputePenetration(boxes[a],boxes[a].transform.position,boxes[a].transform.rotation,boxes[b],boxes[b].transform.position,boxes[b].transform.rotation,out _,out float depth) && depth>.01f)
                    overlaps.Add(boxes[a].name+" / "+boxes[b].name+" "+depth.ToString("F3")+"m");
            }
            Test("Parked collision volumes do not overlap",overlaps.Count==0,string.Join("; ",overlaps));
            foreach(var actor in actors)
            {
                Test(actor.name+" faces travel direction",Vector3.Dot(actor.ModelForward,actor.transform.forward)>.98f,"dot="+Vector3.Dot(actor.ModelForward,actor.transform.forward));
                var motion=actor.GetComponent<TrafficWheelMotion>();int expected=actor.kind==ScenarioKind.CarCutIn?12:6;
                Test(actor.name+" all LOD wheels animated",motion && motion.wheels!=null && motion.wheels.Length==expected && motion.wheels.All(w=>w && w.GetComponentInChildren<MeshFilter>(true)) && motion.wheels.Distinct().Count()==expected,"expected="+expected+" actual="+(motion?motion.wheels.Length:0));
            }
            var street=GameObject.Find(StreetLifeSetup.RootName);
            Test("Street dressing present and visual only",street && street.GetComponentsInChildren<Collider>(true).Length==0 && street.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==2),"No added collision or radar occlusion");
            string json=JsonUtility.ToJson(new Report{capturedAt=DateTime.UtcNow.ToString("O"),checks=checks.ToArray()},true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,json);
            return checks.Count(c=>c.passed)+"/"+checks.Count+" passed; "+string.Join("; ",checks.Where(c=>!c.passed).Select(c=>c.name+": "+c.detail));
        }
    }
}

