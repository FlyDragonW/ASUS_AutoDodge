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
    // Anonymous visible fragments only. See Sources/anonymous-gap-2025.json.
    // No tenant lettering, historic identity, full shop plan or measured dimensions are inferred.
    public static class AnonymousGapSetup
    {
        public const string RootName = "Anonymous north gaps • observed January 2025";
        static string art;
        static float width;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Geometry> geometry = new Dictionary<string, Geometry>();

        [Serializable] sealed class Survey { public Building[] buildings; }
        [Serializable] sealed class Building { public string id; public Edge[] roadFrontEdges; }
        [Serializable] sealed class Edge { public string roadId; public int edgeIndex; public Vector3 outward; }
        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> indices = new List<int>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int n = vertices.Count;
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                foreach (Vector3 p in new[] { a, b, c, d })
                {
                    vertices.Add(p);
                    uv.Add(Mathf.Abs(normal.y) > .7f ? new Vector2(p.x, p.z) :
                        Mathf.Abs(normal.x) > .7f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                }
                indices.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            }
        }
        static Geometry G(string key)
        {
            if (!geometry.TryGetValue(key, out var value)) geometry[key] = value = new Geometry();
            return value;
        }
        static void M(string key, string html, float metallic = 0f, float smoothness = .18f)
        {
            string path = art + "/" + key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            ColorUtility.TryParseHtmlString(html, out var color);
            material.name = key;
            material.enableInstancing = true;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            materials[key] = material;
            EditorUtility.SetDirty(material);
        }
        static void Box(string key, float x, float y, float z, float w, float h, float depth)
        {
            Geometry g = G(key);
            Vector3 p = new Vector3(x, y, z), half = new Vector3(w, h, depth) * .5f;
            Vector3 P(float a, float b, float c) => p + Vector3.Scale(new Vector3(a, b, c), half);
            g.Quad(P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),P(1,-1,-1));
            g.Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1));
            g.Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));
            g.Quad(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));
            g.Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1));
            g.Quad(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1));
        }
        static void Materials()
        {
            // Restrained swatches estimated from the night footage; no calibrated colour claim.
            M("Wall", "#777873"); M("Fascia", "#55575A"); M("FasciaRim", "#343A3B", .2f, .23f);
            M("Apron", "#59514C", .12f); M("Shutter", "#48504E", .16f, .23f);
            M("ShutterRib", "#59605C", .22f, .26f); M("Frame", "#4A5352", .18f, .24f);
            M("Inside", "#596766"); M("PalePanel", "#ABB3AF"); M("Recess", "#303D3C");
            M("Floor", "#8C928C");
        }
        static void GrayClosedFragment()
        {
            // First interval immediately south of Mr. Wish, approximately the 134 address region.
            // Scooter occlusion prevents a lower-facade survey: keep that region a plain closure.
            Box("Shutter", 0, 1.405f, -.11f, width - .28f, 2.61f, .08f);
            for (int row = 0; row < 24; row++)
                Box("ShutterRib", 0, .20f + row * .105f, -.061f, width - .34f, .014f, .009f);
            foreach (float side in new[] { -1f, 1f })
            {
                Box("Wall", side * (width * .5f - .056f), 1.48f, -.11f, .112f, 2.96f, .25f);
                Box("Frame", side * (width * .5f - .148f), 1.425f, -.052f, .058f, 2.71f, .10f);
            }
            Box("Apron", 0, 2.965f, .016f, width, .43f, .24f);
            Box("FasciaRim", 0, 3.225f, .05f, width, .07f, .40f);
            Box("FasciaRim", 0, 3.59f, -.056f, width, .65f, .145f);
            Box("Fascia", 0, 3.59f, .025f, width - .095f, .568f, .035f);
            // No guessed characters or abstract substitute lettering on the blank gray panel.
        }
        static void NarrowEntranceFragment()
        {
            // NOT the white round sign on the other side of the telecom frontage.
            // This is the small pale/dark recess immediately north of the green 138 frontage.
            // A .75m shallow closure merely prevents an open back; interior layout is unresolved.
            Box("Inside", 0, 1.43f, -.77f, width, 2.70f, .08f);
            Box("Recess", .20f, 1.455f, -.72f, width * .48f, 2.61f, .022f);
            Box("Floor", 0, .075f, -.38f, width, .13f, .82f);
            Box("Wall", 0, 2.81f, -.34f, width, .13f, .88f);
            foreach (float side in new[] { -1f, 1f })
                Box("Wall", side * (width * .5f - .045f), 1.43f, -.34f, .09f, 2.70f, .88f);
            // Broad pale side panel and narrow opposite jamb are visible; their functions are unknown.
            Box("PalePanel", -width * .32f, 1.44f, -.25f, width * .29f, 2.64f, .07f);
            Box("Frame", -width * .165f, 1.44f, -.199f, .045f, 2.69f, .065f);
            Box("PalePanel", width * .5f - .10f, 1.44f, -.17f, .20f, 2.64f, .13f);
            Box("Frame", width * .5f - .215f, 1.44f, -.093f, .035f, 2.69f, .038f);
            Box("Frame", .16f, 2.765f, -.145f, width * .67f, .08f, .13f);
            Box("Apron", 0, 3.055f, -.055f, width, .48f, .24f);
            Box("FasciaRim", 0, 3.306f, .016f, width, .045f, .28f);
            // No door handles, lettering, logo, merchandise or upper-storey decoration is asserted.
        }
        public static GameObject Build(Transform parent)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Build anonymous facade fragments in Edit mode.");
            if (!parent) throw new ArgumentNullException(nameof(parent));
            var entries = SurveyStreetscapeSetup.LoadAdditionalFrontages()
                .Where(e => e.id != null && e.id.StartsWith("AG25", StringComparison.Ordinal)).OrderBy(e => e.id).ToArray();
            if (!new[] { "AG2501", "AG2502" }.SequenceEqual(entries.Select(e => e.id)))
                throw new InvalidOperationException("Merge the two anonymous-gap-reservations.json groups before building AG25.");
            var survey = JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            Vector3 normal = survey.buildings.Single(b => b.id == "1287937529").roadFrontEdges
                .Single(e => e.edgeIndex == 2 && e.roadId == "316698035").outward.normalized;
            var old = parent.Cast<Transform>().FirstOrDefault(t => t.name == RootName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(RootName); root.layer = 2; root.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(root, "Build anonymous January frontage fragments");
            foreach (var entry in entries)
            {
                var shop = new GameObject(entry.sceneName); shop.layer = 2; shop.transform.SetParent(root.transform, false);
                shop.transform.localPosition = entry.center; shop.transform.localRotation = Quaternion.LookRotation(normal, Vector3.up);
                art = "Assets/TaiwanRider/Art/StreetSurvey/AnonymousGap/" + entry.id;
                Directory.CreateDirectory(art); AssetDatabase.Refresh();
                geometry.Clear(); materials.Clear(); width = entry.width; Materials();
                if (entry.id == "AG2501") GrayClosedFragment(); else NarrowEntranceFragment();
                foreach (Vector3 v in geometry.Values.SelectMany(g => g.vertices))
                    if (Mathf.Abs(v.x) > width * .5f + .001f || v.y > Mathf.Max(entry.height, entry.visualHeight) + .001f)
                        throw new InvalidOperationException(entry.id + " exceeds its reserved width or height.");
                SaveMeshes(shop.transform);
            }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(root.scene); return root;
        }
        static void SaveMeshes(Transform parent)
        {
            foreach (var pair in geometry)
            {
                string path = art + "/AnonymousGap_" + pair.Key + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); bool exists = mesh;
                if (exists) mesh.Clear(); else mesh = new Mesh();
                mesh.name = "AnonymousGap_" + pair.Key; mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(pair.Value.vertices); mesh.SetUVs(0, pair.Value.uv); mesh.SetTriangles(pair.Value.indices, 0);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); mesh.UploadMeshData(false);
                if (exists) EditorUtility.SetDirty(mesh); else AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject("Anonymous frontage batch • " + pair.Key); go.layer = 2; go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                bool structural = pair.Key == "Wall" || pair.Key == "Inside";
                var material = materials[pair.Key];
                if (material.HasProperty("_ReceiveShadows"))
                {
                    material.SetFloat("_ReceiveShadows", structural ? 1f : 0f);
                    if (structural) material.DisableKeyword("_RECEIVE_SHADOWS_OFF"); else material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                    EditorUtility.SetDirty(material);
                }
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = structural ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = structural;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
