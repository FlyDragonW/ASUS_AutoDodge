using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TaiwanRider.Editor
{
    /// <summary>Corrects Lane 90's visible centreline from the cached OSM survey.
    /// Width and elevation remain prototype assumptions, not surveyed site dimensions.</summary>
    public static class SiteLaneSetup
    {
        public const string RootName = "Lane 90 • OSM centreline visual correction";
        const string AssetRoot = "Assets/TaiwanRider";
        const string Generated = AssetRoot + "/Art/SiteMatch";
        const string MeshPath = Generated + "/Lane90_OSMCentreline.asset";
        // Existing prototype dimensions are retained explicitly; OSM has no width/elevation tags.
        const float PrototypeWidth = 9f;
        const float StartX = 5.5f;
        const float SurfaceY = .003f;
        [Serializable] sealed class Survey { public Vector3[] lanePoints; }

        [MenuItem("Taiwan Rider/Site Match/Apply Lane 90 centreline")]
        public static void ApplyMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != AssetRoot + "/Scenes/GongguanRide.unity")
                throw new InvalidOperationException("Open GongguanRide outside Play mode first.");

            var street = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Street geometry");
            var original = street ? street.transform.Find("Lane 90") : null;
            var originalRenderer = original ? original.GetComponent<MeshRenderer>() : null;
            if (!originalRenderer || !original.GetComponent<Collider>())
                throw new InvalidOperationException("Expected Street geometry/Lane 90 with its original renderer and collider.");
            if (!originalRenderer.sharedMaterial)
                throw new InvalidOperationException("The original Lane 90 asphalt material is missing.");

            // The old detail generator merges its invented alley buildings with corner facades.
            // Rebuild that generator with includeAlleyBackdrop:false before applying this pass.
            bool oldBackdrop = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
                .Any(r => r.name == "Buildings_and_pavement_BackdropAsphalt" && r.enabled && r.gameObject.activeInHierarchy);
            if (oldBackdrop)
                throw new InvalidOperationException("Rebuild IntersectionDetailSetup with includeAlleyBackdrop:false first; its straight alley and invented terminal wall are still active.");

            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetRoot + "/Data/RouteSurvey.json");
            if (!source) throw new InvalidOperationException("RouteSurvey.json is missing.");
            var survey = JsonUtility.FromJson<Survey>(source.text);
            Vector3[] points = ValidateAndTrim(survey == null ? null : survey.lanePoints);
            Mesh pending = BuildSurface(points);

            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Correct Lane 90 visible centreline");
            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (old) Undo.DestroyObjectImmediate(old);

            // Retain the box, its enabled collider, transform, layer and physics properties.
            // The hazard starts at x=13.05,z=0 and still runs over the same original near ground.
            Undo.RecordObject(originalRenderer, "Hide the straight prototype lane surface");
            originalRenderer.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(originalRenderer);

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh)
            {
                mesh.Clear();
                mesh.vertices = pending.vertices;
                mesh.uv = pending.uv;
                mesh.triangles = pending.triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                UnityEngine.Object.DestroyImmediate(pending);
                EditorUtility.SetDirty(mesh);
            }
            else { mesh = pending; AssetDatabase.CreateAsset(mesh, MeshPath); }
            mesh.UploadMeshData(false);

            var root = new GameObject(RootName) { layer = 2 };
            Undo.RegisterCreatedObjectUndo(root, "Create corrected Lane 90 visual surface");
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = originalRenderer.sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);
            // Visual correction only: no collider, props, new building or inferred lane connection.
            Undo.CollapseUndoOperations(undo);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return $"Lane 90 visual surface follows {points.Length} cached OSM points to x={points[points.Length-1].x:F3}, z={points[points.Length-1].z:F3} m. Width remains the unmeasured {PrototypeWidth:F1} m prototype width. Original near collider and hazards are unchanged; no distant collider or invented buildings added. Scene is unsaved.";
        }

        static Vector3[] ValidateAndTrim(Vector3[] source)
        {
            if (source == null || source.Length < 2 || source[0].sqrMagnitude > .0001f)
                throw new InvalidOperationException("Lane survey must begin at the existing shared OSM origin.");
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 p = source[i];
                if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z) ||
                    Mathf.Abs(p.y) > .001f || p.magnitude > 300f || (i > 0 && p.x <= source[i - 1].x))
                    throw new InvalidOperationException("Lane survey is not the expected finite eastward local polyline.");
            }
            if (source[source.Length - 1].x <= StartX)
                throw new InvalidOperationException("Lane survey ends before the existing road connection.");
            var result = new List<Vector3>();
            for (int i = 1; i < source.Length; i++)
            {
                if (source[i].x <= StartX) continue;
                if (result.Count == 0)
                    result.Add(Vector3.Lerp(source[i - 1], source[i],
                        (StartX - source[i - 1].x) / (source[i].x - source[i - 1].x)));
                result.Add(source[i]);
            }
            return result.ToArray();
        }

        static Mesh BuildSurface(Vector3[] points)
        {
            var vertices = new Vector3[points.Length * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(points.Length - 1) * 6];
            float distance = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 previous = (points[i] - points[Mathf.Max(0, i - 1)]).normalized;
                Vector3 next = (points[Mathf.Min(points.Length - 1, i + 1)] - points[i]).normalized;
                if (i == 0) previous = next;
                if (i == points.Length - 1) next = previous;
                Vector3 before = Vector3.Cross(previous, Vector3.up).normalized;
                Vector3 after = Vector3.Cross(next, Vector3.up).normalized;
                Vector3 normal = (before + after).normalized;
                float alignment = Vector3.Dot(normal, after);
                if (alignment < .8f) throw new InvalidOperationException("Unexpected sharp bend in Lane 90 survey.");
                Vector3 offset = normal * (PrototypeWidth * .5f / alignment);
                Vector3 center = points[i] + Vector3.up * SurfaceY;
                vertices[i * 2] = center - offset;
                vertices[i * 2 + 1] = center + offset;
                if (i > 0) distance += Vector3.Distance(points[i - 1], points[i]);
                uv[i * 2] = new Vector2(0, distance / 3f);
                uv[i * 2 + 1] = new Vector2(PrototypeWidth / 3f, distance / 3f);
                if (i == points.Length - 1) continue;
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            var mesh = new Mesh { name = "Lane90_OSMCentreline" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
