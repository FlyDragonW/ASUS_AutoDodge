using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TaiwanRider.Editor
{
    /// <summary>Read-only integration checks for the refined scene; driving remains covered by DemoAcceptance.</summary>
    public static class RealismValidation
    {
        const string HeroAsset = "Assets/TaiwanRider/Art/Models/Scooter125_Hero.fbx";
        [Serializable] public sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] public sealed class Report
        {
            public string capturedAtUtc, scene, mode;
            public int passed, failed, heroRenderers, heroTriangles;
            public List<Check> checks = new List<Check>();
        }

        static void Test(Report report, string name, bool passed, string detail)
        {
            report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            if (passed) report.passed++; else report.failed++;
        }
        static bool IsUnder(Transform part, Transform root) => part && root && (part == root || part.IsChildOf(root));
        static T[] InScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        static bool RenderMaterialsValid(Renderer renderer) => renderer.sharedMaterials.Length > 0 &&
            renderer.sharedMaterials.All(material => material && material.shader && material.shader.name != "Hidden/InternalErrorShader");

        public static string Validate(bool requireRefinement = true)
        {
            var scene = SceneManager.GetActiveScene();
            var report = new Report { capturedAtUtc = DateTime.UtcNow.ToString("O"), scene = scene.path, mode = Application.isPlaying ? "Play" : "Edit" };
            Test(report, "Expected scene", scene.path == "Assets/TaiwanRider/Scenes/GongguanRide.unity", scene.path);
            var sessions = InScene<ScenarioRunner>(scene).Where(s => s.gameObject.activeInHierarchy).ToArray();
            Test(report, "One active scenario runner", sessions.Length == 1, sessions.Length.ToString());
            if (sessions.Length != 1) return JsonUtility.ToJson(report, true);
            var session = sessions[0]; var rider = session.rider;
            Test(report, "Player and route references", rider && session.route && rider.session == session, "ScenarioRunner -> ScooterController -> ScenarioRunner and RoadRoute");
            if (!rider || !session.route) return JsonUtility.ToJson(report, true);

            var route = session.route;
            bool validRoute = route.points != null && route.points.Length >= 2 &&
                route.points.Zip(route.points.Skip(1), (a, b) => b.z > a.z).All(value => value);
            Test(report, "Road centreline remains ordered", validRoute, route.points == null ? "null" : route.points.Length + " survey points");
            Test(report, "Player definition and speed", rider.definition && Mathf.Approximately(rider.definition.topSpeedKph, 100), rider.definition ? rider.definition.topSpeedKph + " km/h" : "missing definition");
            var chassis = rider.GetComponent<BoxCollider>();
            Test(report, "Original player chassis", chassis && chassis.enabled && !chassis.isTrigger && rider.GetComponent<Rigidbody>() && rider.gameObject.layer == 8 &&
                Vector3.Distance(chassis.size, new Vector3(.54f, 1.18f, 1.55f)) < .001f && Vector3.Distance(chassis.center, new Vector3(0, .59f, 0)) < .001f,
                chassis ? "size " + chassis.size + ", centre " + chassis.center : "missing chassis");
            bool linked = rider.visualBody && rider.visualBody.gameObject.activeInHierarchy &&
                IsUnder(rider.frontSteering, rider.visualBody) && IsUnder(rider.frontVisualWheel, rider.visualBody) && IsUnder(rider.rearVisualWheel, rider.visualBody) &&
                rider.frontSteering.gameObject.activeInHierarchy && rider.frontVisualWheel.gameObject.activeInHierarchy && rider.rearVisualWheel.gameObject.activeInHierarchy;
            Test(report, "Active lean, steering and wheel references", linked, "Inactive backup objects cannot satisfy this check");
            Test(report, "First-person camera connection", IsUnder(rider.cameraRig, rider.transform) && IsUnder(rider.riderCamera ? rider.riderCamera.transform : null, rider.cameraRig), "Player -> eye rig -> rider camera");
            Test(report, "Physical wheel references", rider.frontWheel && rider.rearWheel && rider.frontWheel != rider.rearWheel && IsUnder(rider.frontWheel.transform, rider.transform) && IsUnder(rider.rearWheel.transform, rider.transform), "Both authoring WheelColliders retained");
            if (rider.frontVisualWheel && rider.rearVisualWheel && rider.frontWheel && rider.rearWheel && rider.definition)
            {
                var front = rider.transform.InverseTransformPoint(rider.frontVisualWheel.position);
                var rear = rider.transform.InverseTransformPoint(rider.rearVisualWheel.position);
                float wheelbase = front.z - rear.z;
                float frontError = Vector3.Distance(rider.frontVisualWheel.position, rider.frontWheel.transform.position);
                float rearError = Vector3.Distance(rider.rearVisualWheel.position, rider.rearWheel.transform.position);
                Test(report, "Visual wheels face forward at metric wheelbase", wheelbase > 0 && Mathf.Abs(wheelbase - rider.definition.wheelbase) < .06f,
                    "visual " + wheelbase.ToString("F3") + " m; definition " + rider.definition.wheelbase.ToString("F3") + " m");
                Test(report, "Visual and physical axle alignment", frontError < .12f && rearError < .12f,
                    "front " + frontError.ToString("F3") + " m; rear " + rearError.ToString("F3") + " m");
            }

            var activeParts = rider.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.activeInHierarchy).ToArray();
            var hero = activeParts.FirstOrDefault(t => t.name == "Player scooter • detailed 125" || t.name == "Scooter125_Hero");
            if (requireRefinement)
            {
                Test(report, "Hero FBX imported", AssetDatabase.LoadAssetAtPath<GameObject>(HeroAsset), HeroAsset);
                Test(report, "Refined scooter active", hero, "Player scooter • detailed 125 / Scooter125_Hero");
            }
            if (hero)
            {
                var renderers = hero.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.activeInHierarchy).ToArray();
                report.heroRenderers = renderers.Length;
                foreach (var mesh in hero.GetComponentsInChildren<MeshFilter>(true).Where(m => m.gameObject.activeInHierarchy && m.sharedMesh))
                    for (int submesh = 0; submesh < mesh.sharedMesh.subMeshCount; submesh++) report.heroTriangles += (int)mesh.sharedMesh.GetIndexCount(submesh) / 3;
                Test(report, "Hero has renderable geometry", report.heroRenderers > 0 && report.heroTriangles > 0, report.heroRenderers + " renderers, " + report.heroTriangles + " triangles");
                Test(report, "Hero materials resolved", renderers.Length > 0 && renderers.All(RenderMaterialsValid), "No null material or missing shader");
                Test(report, "Hero excluded from own radar", hero.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.activeInHierarchy).All(t => t.gameObject.layer == 8), "All active hero parts use player layer 8");
                Test(report, "Hero adds no collision hulls", !hero.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeInHierarchy), "Physics remains on the original chassis");
                Test(report, "Animated parts reference hero", IsUnder(rider.frontSteering, hero) && IsUnder(rider.frontVisualWheel, hero) && IsUnder(rider.rearVisualWheel, hero), "Steering/front/rear do not reference inactive source model");
            }
            var mirrors = rider.GetComponentsInChildren<MirrorView>(true).Where(m => m.gameObject.activeInHierarchy && m.enabled).ToArray();
            Test(report, "Two linked live mirrors", mirrors.Length == 2 && mirrors.All(m => m.surface && m.surface.gameObject.activeInHierarchy && m.mirrorCamera && IsUnder(m.mirrorCamera.transform, rider.transform)), mirrors.Length + " active MirrorView components");
            if (hero) Test(report, "Mirrors reference refined surfaces", mirrors.Length == 2 && mirrors.All(m => IsUnder(m.surface ? m.surface.transform : null, hero)), "No surface points into inactive backup");
            Test(report, "Mirror cameras exclude player", mirrors.Length == 2 && mirrors.All(m => m.mirrorCamera && (m.mirrorCamera.cullingMask & (1 << 8)) == 0 && !m.mirrorCamera.enabled), "Mirrors render by explicit URP request");
            Test(report, "Build-safe mirror and radar materials", Resources.Load<Material>("RiderMirror") && Resources.Load<Material>("RiderRadar"), "Resources/RiderMirror and Resources/RiderRadar");

            var detailRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Intersection detail • reference-informed v1" && root.activeInHierarchy);
            var surveyRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Gongguan surveyed street • 2025 visual reconstruction" && root.activeInHierarchy);
            if (requireRefinement) Test(report, "Refined intersection active", detailRoot || surveyRoot, "Active intersection detail or surveyed street reconstruction");
            if (detailRoot)
            {
                var detailRenderers = detailRoot.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.activeInHierarchy).ToArray();
                Test(report, "Intersection materials resolved", detailRenderers.Length > 0 && detailRenderers.All(RenderMaterialsValid), detailRenderers.Length + " active material batches");
                Test(report, "Intersection decoration adds no colliders", detailRoot.GetComponentsInChildren<Collider>(true).Length == 0, "Original road and building collision proxies remain authoritative");
                Test(report, "Intersection detail layer", detailRoot.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 2), "Visual-only detail uses Ignore Raycast layer 2");
                var oldNames = new[] { "Building_-1_11", "Building_-1_12", "Building_-1_13", "Building_-1_14", "Building_-1_15", "Building_1_39", "Building_1_40", "Building_1_41", "Building_1_42" };
                var oldBuildings = InScene<Transform>(scene).Where(t => oldNames.Contains(t.name)).ToArray();
                Test(report, "Replaced facade visuals do not overlap", oldBuildings.Length == oldNames.Length && oldBuildings.All(t => !t.gameObject.activeInHierarchy),
                    oldBuildings.Length + " old facade groups retained inactive");
                var proxies = InScene<Collider>(scene).Where(c => oldNames.Any(name => c.name == name + " collision")).ToArray();
                Test(report, "Original building collision proxies retained", proxies.Length == oldNames.Length && proxies.All(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger),
                    proxies.Length + " collision proxies remain enabled");
            }
            if (surveyRoot)
            {
                var surveyRenderers = surveyRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                Test(report, "Survey materials resolved", surveyRenderers.Length > 0 && surveyRenderers.All(RenderMaterialsValid),
                    surveyRenderers.Length + " enabled material batches; no null material or missing shader");
                Test(report, "Survey renderer layer", surveyRenderers.All(r => r.gameObject.layer == 2),
                    "All enabled visual renderers use Ignore Raycast layer 2");
                Test(report, "Survey renderers add no colliders", surveyRenderers.All(r => r.GetComponents<Collider>().Length == 0),
                    "Collision geometry remains separate from visual renderer objects");
                var surveyCollisionRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Surveyed footprint collisions" && root.activeInHierarchy);
                var surveyColliders = surveyCollisionRoot ? surveyCollisionRoot.GetComponentsInChildren<Collider>(true) : new Collider[0];
                Test(report, "Survey footprint collision root retained", surveyCollisionRoot && surveyColliders.Length > 0 &&
                    surveyColliders.All(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger),
                    surveyColliders.Length + " active, solid colliders under the independent Surveyed footprint collisions root");
            }

            foreach (var actor in new[] { session.car, session.scooter })
            {
                Test(report, "Hazard collision reference: " + (actor ? actor.name : "missing"), actor && actor.bodyCollider && IsUnder(actor.bodyCollider.transform, actor.transform) && !actor.bodyCollider.isTrigger && actor.GetComponent<Rigidbody>(), "Hazard roots may be inactive in menu");
                if (actor) Test(report, "Hazard forward alignment: " + actor.name, Vector3.Dot(actor.ModelForward, actor.transform.forward) > .98f, "No reversed hazard model");
            }
            // Exclude dynamic actors when sampling road support. This stays valid during a driving run.
            if (validRoute)
            {
                var unsupported = new List<string>();
                foreach (float z in new[] { session.startPosition.z, -10f, 0f, 10f, session.finishZ })
                {
                    var origin = route.Position(z, 2.65f, 1.2f);
                    bool support = Physics.RaycastAll(origin, Vector3.down, 1.6f, ~(1 << 8), QueryTriggerInteraction.Ignore)
                        .Any(hit => hit.normal.y > .9f && hit.point.y > -.2f && hit.point.y < .25f && !hit.collider.GetComponentInParent<HazardActor>());
                    if (!support) unsupported.Add(z.ToString("F1"));
                }
                Test(report, "Road collision support retained", unsupported.Count == 0, unsupported.Count == 0 ? "Ground under start, approach, intersection, exit and finish" : "No support at z: " + string.Join(", ", unsupported));
            }
            Test(report, "Radar resolution preserved", RadarSensor.BeamCount == 68, RadarSensor.BeamCount + " rays");
            if (Application.isPlaying)
            {
                var demo = session.Demo;
                Test(report, "Runtime autopilot and radar links", demo && demo.Sensor && demo.Pilot && demo.Sensor.rider == rider && demo.Sensor.session == session && demo.Pilot.rider == rider && demo.Pilot.radar == demo.Sensor && demo.Pilot.session == session && ReferenceEquals(rider.AutomaticInput, demo.Pilot), "Scenario -> demo -> sensor/pilot -> player");
                Test(report, "Runtime active camera link", demo && demo.ViewCamera && demo.ActiveCamera && demo.ActiveCamera.enabled && demo.ThirdPerson == demo.ViewCamera.enabled && demo.ThirdPerson != rider.riderCamera.enabled, "Exactly the selected riding camera is enabled");
                Test(report, "Runtime display link", demo && demo.Display && demo.Display.demo == demo && demo.Display.Visible == session.RadarVisualization, "Ray visibility setting remains independent from sensing");
                Test(report, "Runtime crash connection", rider.Crash && rider.Body && rider.Crash == rider.GetComponent<RiderCrash>(), "Existing ejection controller retained");
                Test(report, "Runtime live mirror targets", mirrors.Length == 2 && mirrors.All(m => m.mirrorCamera && m.mirrorCamera.targetTexture && m.surface && m.surface.sharedMaterial && m.surface.sharedMaterial.GetTexture("_BaseMap") == m.mirrorCamera.targetTexture), "Each refined mirror surface receives its camera texture");
            }
            return JsonUtility.ToJson(report, true);
        }

        public static string SaveReport(string path = "Validation/Realism/integration.json", bool requireRefinement = true)
        {
            string json = Validate(requireRefinement);
            string folder = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, json); return json;
        }
    }
}
