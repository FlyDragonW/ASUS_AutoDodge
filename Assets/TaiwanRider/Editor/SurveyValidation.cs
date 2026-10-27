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
    /// <summary>Read-only scene checks for the geographic footprint reconstruction.</summary>
    public static class SurveyValidation
    {
        const string SurveyRoot = "Gongguan surveyed street • 2025 visual reconstruction";
        const string CollisionRoot = "Surveyed footprint collisions";
        // Same public root value as ShuiyuanMarketSetup.RootName; no dependency on its builder internals.
        const string MarketRoot = "Shuiyuan Market • late-2025 exterior study • OSM footprint";
        [Serializable] sealed class Document { public Building[] buildings; }
        [Serializable] sealed class Building
        {
            public string id, name, heightSource, levelSource, buildingType;
            public Vector3[] points;
            public int levels, minimumLevels;
            public float heightMetres;
            public bool hasMeasuredHeight, isRoofOnly;
        }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class ParkingConflict { public string vehicle, buildingId, buildingName; }
        [Serializable] sealed class Report
        {
            public string timeUtc, scene, ridingPhysicsHash;
            public int passed, failed, datasetBuildings, sceneBuildingGroups, activeParkedVehicles;
            public List<Check> checks = new List<Check>();
            public List<ParkingConflict> parkingConflicts = new List<ParkingConflict>();
        }
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static T[] All<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static void Test(Report report, string name, bool passed, string detail)
        {
            report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            if (passed) report.passed++; else report.failed++;
        }

        // Compare before/after in Edit mode. New building walls and reparked vehicles are
        // deliberately excluded; player, hazards and the main driving surface remain covered.
        public static string RidingPhysicsHash()
        {
            var rows = All<Collider>().Where(c => c.name == "Tingzhou metric road" ||
                c.GetComponentInParent<ScooterController>() || c.GetComponentInParent<HazardActor>())
                .Select(c => PathOf(c.transform) + "|" + c.GetType().Name + "|" + c.gameObject.layer + "|" +
                    c.gameObject.activeInHierarchy + "|" + c.transform.position.ToString("F6") + "|" +
                    c.transform.rotation.ToString("F6") + "|" + c.transform.lossyScale.ToString("F6") + "|" +
                    EditorJsonUtility.ToJson(c));
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                    string.Join("\n", rows.OrderBy(v => v, StringComparer.Ordinal))))).Replace("-", "");
        }

        public static string Save(string path = "Validation/StreetSurvey/integration.json", string expectedRidingPhysicsHash = null)
        {
            var scene = SceneManager.GetActiveScene();
            var report = new Report { timeUtc = DateTime.UtcNow.ToString("O"), scene = scene.path, ridingPhysicsHash = RidingPhysicsHash() };
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TaiwanRider/Data/StreetSurvey.json");
            if (!asset) throw new InvalidOperationException("StreetSurvey.json is missing.");
            var data = JsonUtility.FromJson<Document>(asset.text);
            if (data == null || data.buildings == null) throw new InvalidOperationException("StreetSurvey has no buildings array.");
            var buildings = data.buildings;
            report.datasetBuildings = buildings.Length;
            Test(report, "Dataset contains 56 unique footprints", buildings.Length == 56 && buildings.Select(b => b.id).Distinct().Count() == 56,
                buildings.Length + " source polygons; no random frontage modules");
            Test(report, "Footprint rings are finite CCW polygons", buildings.All(b => b.points != null && b.points.Length >= 3 &&
                b.points.All(p => Finite(p.x) && Finite(p.y) && Finite(p.z)) && SignedArea(b.points) > .01f),
                "XZ counter-clockwise; implicit closing edge; coordinates in world metres");
            Test(report, "Unknown height remains separate from floor evidence", buildings.All(b => b.levels >= 0 && b.minimumLevels >= 0 &&
                b.heightMetres >= 0 && (!b.hasMeasuredHeight || b.heightMetres > 0) && !string.IsNullOrEmpty(b.heightSource) && !string.IsNullOrEmpty(b.levelSource)),
                $"OSM levels: {buildings.Count(b => b.levels > 0)}; lower-bound only: {buildings.Count(b => b.levels == 0 && b.minimumLevels > 0)}; unknown: {buildings.Count(b => b.levels == 0 && b.minimumLevels == 0)}. Minimum levels are not total storeys.");
            Test(report, "No invented measured heights in current dataset", buildings.All(b => !b.hasMeasuredHeight && b.heightMetres == 0),
                "The acquired OSM snapshot has no metre-height tags; any rendering height must have separate estimate provenance.");

            var roots = scene.GetRootGameObjects();
            var surveyRoots = roots.Where(g => g.name == SurveyRoot && g.activeInHierarchy).ToArray();
            Test(report, "One active surveyed street root", surveyRoots.Length == 1, SurveyRoot);
            var root = surveyRoots.FirstOrDefault();
            var groups = root ? root.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.activeInHierarchy &&
                t.name.StartsWith("OSM ", StringComparison.Ordinal)).ToArray() : new Transform[0];
            report.sceneBuildingGroups = groups.Length;
            foreach (var building in buildings)
            {
                int count = groups.Count(t => t.name.StartsWith("OSM " + building.id + " • ", StringComparison.Ordinal));
                Test(report, "One visual group: " + building.id, count == 1, count + " active groups; " + building.name);
            }
            Test(report, "Market appears exactly once", groups.Count(t => t.name.StartsWith("OSM 232744652 • ", StringComparison.Ordinal)) == 1,
                "Shuiyuan market has one host building group; no duplicate generic envelope");
            var marketModels = All<Transform>().Where(t => t.name == MarketRoot && t.gameObject.activeInHierarchy).ToArray();
            Test(report, "One actual active market model", marketModels.Length == 1, MarketRoot);
            var marketModel = marketModels.FirstOrDefault();
            var marketMeshes = marketModel ? marketModel.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                .Select(r => r.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh && f.sharedMesh.vertexCount > 0).ToArray() : new MeshFilter[0];
            Test(report, "Actual market model has rendered geometry", marketMeshes.Length > 0,
                marketMeshes.Length + " non-empty enabled mesh renderers; " + marketMeshes.Sum(f => f.sharedMesh.vertexCount) + " vertices. The empty OSM host alone does not pass.");
            if (root)
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.activeInHierarchy && r.enabled).ToArray();
                Test(report, "All active survey materials resolve", renderers.Length > 0 && renderers.All(r => r.sharedMaterials.Length > 0 &&
                    r.sharedMaterials.All(m => m && m.shader && m.shader.name != "Hidden/InternalErrorShader")), renderers.Length + " active renderers");
                Test(report, "Visual meshes add no MeshCollider", root.GetComponentsInChildren<MeshCollider>(true).Length == 0,
                    "Footprint collision walls use separate BoxColliders");
                Test(report, "Rendered survey objects carry no physics colliders", renderers.All(r => r.GetComponents<Collider>().Length == 0),
                    "The separately named ground support BoxCollider is allowed; architectural render objects remain visual only");
            }

            var collisionRoots = All<Transform>().Where(t => t.name == CollisionRoot && t.gameObject.activeInHierarchy).ToArray();
            Test(report, "One active footprint collision root", collisionRoots.Length == 1, CollisionRoot);
            var collision = collisionRoots.FirstOrDefault();
            if (collision)
            {
                var colliders = collision.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray();
                Test(report, "Footprint collisions are non-trigger BoxColliders", colliders.Length > 0 && colliders.All(c => c is BoxCollider && !c.isTrigger),
                    colliders.Length + " active walls");
                foreach (var building in buildings.Where(b => b.isRoofOnly))
                    Test(report, "Roof-only feature has no solid collision: " + building.id,
                        !colliders.Any(c => PathOf(c.transform).Contains(building.id)), "building=roof is not a closed multi-storey house");
            }
            var fake = All<BoxCollider>().Where(c => c.name.StartsWith("Building_", StringComparison.Ordinal) && c.name.EndsWith(" collision", StringComparison.Ordinal)).ToArray();
            Test(report, "All 55 old random building colliders retained inactive", fake.Length == 55 && fake.All(c => !c.enabled || !c.gameObject.activeInHierarchy),
                fake.Length + " old collision proxies; active=" + fake.Count(c => c.enabled && c.gameObject.activeInHierarchy));
            var lane160 = All<MeshFilter>().Where(f => f.gameObject.activeInHierarchy && f.sharedMesh &&
                (PathOf(f.transform).IndexOf("160", StringComparison.OrdinalIgnoreCase) >= 0 || PathOf(f.transform).Contains("59683808")))
                .Where(f => f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled).ToArray();
            Test(report, "Lane 160 visible geometry is active", lane160.Length > 0,
                string.Join(", ", lane160.Select(f => PathOf(f.transform))) + "; exact OSM opposite arm, not a guessed lane");

            var parked = roots.FirstOrDefault(g => g.name == "Parked scooters");
            var parkingRoots = parked ? parked.transform.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy &&
                (t.name.StartsWith("Parked scooter ", StringComparison.Ordinal) || t.name == "Parked sedan")).ToArray() : new Transform[0];
            report.activeParkedVehicles = parkingRoots.Length;
            Test(report, "Parked vehicle fleet present", parkingRoots.Length >= 42, parkingRoots.Length + " active vehicle wrappers");
            int vehiclesWithoutCollider = 0;
            foreach (var vehicle in parkingRoots)
            {
                var boxes = vehicle.GetComponentsInChildren<BoxCollider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray();
                if (boxes.Length == 0) { vehiclesWithoutCollider++; continue; }
                foreach (var b in buildings.Where(b => !b.isRoofOnly))
                {
                    if (!boxes.Any(c => PolygonsOverlap(BoxFootprint(c), b.points))) continue;
                    report.parkingConflicts.Add(new ParkingConflict { vehicle = PathOf(vehicle), buildingId = b.id, buildingName = b.name });
                }
            }
            Test(report, "Parking bounds available for every active vehicle", vehiclesWithoutCollider == 0, vehiclesWithoutCollider + " wrappers lack enabled BoxColliders");
            Test(report, "Parked vehicle collision footprints avoid buildings", report.parkingConflicts.Count == 0,
                report.parkingConflicts.Count + " conflicts; uses transformed oriented box polygons, not only vehicle centres");
            if (!string.IsNullOrEmpty(expectedRidingPhysicsHash))
                Test(report, "Main surface and rider/hazard collision state unchanged", report.ridingPhysicsHash == expectedRidingPhysicsHash, report.ridingPhysicsHash);
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string json = JsonUtility.ToJson(report, true); File.WriteAllText(path, json); return json;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float SignedArea(Vector3[] points)
        {
            float area = 0;
            for (int i = 0; i < points.Length; i++) { var a = points[i]; var b = points[(i + 1) % points.Length]; area += a.x * b.z - b.x * a.z; }
            return area * .5f;
        }
        static float Cross(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        static Vector3[] BoxFootprint(BoxCollider box)
        {
            var points = new List<Vector3>();
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                var p = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f, new Vector3(x, y, z)));
                p.y = 0; if (!points.Any(v => Vector3.Distance(v, p) < .0001f)) points.Add(p);
            }
            points = points.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
            var hull = new List<Vector3>();
            foreach (var p in points) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            int lower = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--) { var p = points[i]; while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }
        static bool Inside(Vector3 p, Vector3[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].z > p.z) != (polygon[j].z > p.z) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.z - polygon[i].z) / (polygon[j].z - polygon[i].z) + polygon[i].x)
                    inside = !inside;
            return inside;
        }
        static bool PolygonsOverlap(Vector3[] a, Vector3[] b)
        {
            if (a.Length < 3 || b.Length < 3) return false;
            if (a.Any(p => Inside(p, b)) || b.Any(p => Inside(p, a))) return true;
            for (int i = 0; i < a.Length; i++) for (int j = 0; j < b.Length; j++)
            {
                Vector3 a0 = a[i], a1 = a[(i + 1) % a.Length], b0 = b[j], b1 = b[(j + 1) % b.Length];
                if (Cross(a0, a1, b0) * Cross(a0, a1, b1) < -.000001f && Cross(b0, b1, a0) * Cross(b0, b1, a1) < -.000001f) return true;
            }
            return false;
        }
    }
}
