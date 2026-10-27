using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TaiwanRider.Editor
{
    [InitializeOnLoad]
    public static class VisionDatasetCaptureV2
    {
        const string Key = "RideReflex.VisionDatasetCaptureV2";
        static VisionDatasetCaptureV2() { EditorApplication.playModeStateChanged += OnPlay; }
        public static void Begin()
        {
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene("Assets/TaiwanRider/Scenes/GongguanRide.unity");
            EditorApplication.EnterPlaymode();
        }
        static void OnPlay(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, false);
            new GameObject("Vision dataset exporter (temporary)").AddComponent<VisionDatasetRecorderV2>();
        }
    }

    [DefaultExecutionOrder(11000)]
    public sealed class VisionDatasetRecorderV2 : MonoBehaviour
    {
        const string Root = @"C:\General\NTU\計網\華碩比賽_自動閃避\VisionTraining\v2";
        const int Width = 960, Height = 540;
        [Serializable] public class Config { public string output; public int train, val, test, seed = 20261012; }
        [Serializable] public class Box { public int id, classId, pixels, x, y, width, height; public float distance; }
        [Serializable] public class Frame
        {
            public string image, split, weather;
            public int seed, index;
            public Vector3 cameraPosition;
            public float cameraYaw, fieldOfView, fogDensity; public int geometryProfile; public bool negative;
            public Box[] boxes;
        }
        class Vehicle
        {
            public Transform root;
            public int id, classId;
            public bool spawned;
            public Material[] paint; public Vector3 baseScale; public Renderer[] vehicleRenderers;
        }
        class Surface { public Renderer renderer; public Material[] rgb, mask; }
        Config config;
        ScenarioRunner session;
        Camera camera;
        UniversalAdditionalCameraData cameraData;
        RenderTexture rgbTarget, maskTarget;
        Texture2D rgbPixels, maskPixels;
        readonly List<Vehicle> vehicles = new List<Vehicle>();
        readonly List<Surface> surfaces = new List<Surface>();
        Light[] suns; readonly List<Transform> clutter = new List<Transform>();
        readonly List<Material> backgrounds = new List<Material>(); readonly List<Color> baseColors = new List<Color>();
        Material[] idMaterials;
        StreamWriter manifest;
        int index;
        bool ready;
        System.Random random;

        IEnumerator Start()
        {
            string configPath=Path.Combine(Root,"capture-config.json");
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-visionCaptureConfig")configPath=args[i+1];
            config = JsonUtility.FromJson<Config>(File.ReadAllText(configPath));
            if(File.Exists(Path.Combine(config.output,"frames.jsonl")))throw new Exception("Refusing to overwrite capture");
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            // Use an in-memory pipeline copy: GPU resident material caching is
            // incompatible with swapping RGB/instance-ID materials in one frame.
            var capturePipeline = Instantiate((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline);
            capturePipeline.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
            QualitySettings.renderPipeline = capturePipeline;
            GPUResidentDrawer.ReinitializeIfNeeded();
            session = FindAnyObjectByType<ScenarioRunner>();
            while (!session || !session.Demo) yield return null;
            session.ShowUI = false;
            session.SuppressResultFiles = true;
            session.Demo.SetRadarVisible(false);
            session.Demo.Display.gameObject.SetActive(false);
            session.ResetToMenu();
            // No game simulation is used as training input: only rendered RGB is saved.
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (behaviour != this && behaviour.GetType().Namespace == "TaiwanRider") behaviour.enabled = false;
            session.rider.gameObject.SetActive(false);
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            Time.timeScale = 0;
            SetupVehicles();
            foreach (var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) group.ForceLOD(0);
            camera = new GameObject("Vision camera").AddComponent<Camera>();
            camera.CopyFrom(session.rider.riderCamera);
            camera.enabled = false;
            camera.cullingMask = ~0;
            camera.nearClipPlane = .15f;
            camera.farClipPlane = 100;
            camera.aspect = (float)Width / Height;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            rgbTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            maskTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            rgbTarget.Create(); maskTarget.Create();
            rgbPixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            maskPixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
            SetupClutter();
            PrepareFogMaterials();
            SetupSurfaces();
            suns = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional).ToArray();
            foreach (string split in new[] { "train", "val", "test" }) {
                Directory.CreateDirectory(Path.Combine(config.output, "images", split));
                Directory.CreateDirectory(Path.Combine(config.output, "labels", split));
            }
            Directory.CreateDirectory(Path.Combine(config.output, "qa-masks"));
            manifest = new StreamWriter(Path.Combine(config.output, "frames.jsonl"), false, new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(config.output, "objects.json"), JsonUtility.ToJson(new ObjectList {
                objects = vehicles.Select(v => new ObjectInfo { id = v.id, classId = v.classId, name = v.root.name }).ToArray()
            }, true));
            for (int warmup = 0; warmup < 15; warmup++) yield return null;
            ready = true;
        }
        [Serializable] public class ObjectInfo { public int id, classId; public string name; }
        [Serializable] public class ObjectList { public ObjectInfo[] objects; }
        void SetupVehicles()
        {
            var parking = GameObject.Find("Parked scooters");
            if (!parking) throw new Exception("Missing parked fleet");
            foreach (Transform root in parking.transform) {
                if (root.name.StartsWith("Parked scooter ")) Register(root, 1, false);
                else if (root.name == "Parked sedan") Register(root, 0, false);
            }
            for (int kind = 0; kind < 2; kind++) {
                var original = kind == 0 ? session.car.gameObject : session.scooter.gameObject;
                original.SetActive(false);
                for (int n = 0; n < 4; n++) {
                    var clone = Instantiate(original);
                    clone.name = (kind == 0 ? "Training car " : "Training motorcycle ") + n;
                    foreach (var b in clone.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                    foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                    Register(clone.transform, kind, true);
                }
            }
            if (vehicles.Count != 53) Debug.Log("VISION vehicle count=" + vehicles.Count);
        }
        void Register(Transform root, int classId, bool spawned)
        {
            var paints = new Dictionary<Material, Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                var materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++) {
                    var original = materials[m];
                    if (!original || !(original.name.StartsWith("SedanBody") || original.name.StartsWith("Hero_Pearl"))) continue;
                    if (!paints.TryGetValue(original, out var copy)) { copy = new Material(original); paints.Add(original, copy); }
                    materials[m] = copy;
                }
                renderer.sharedMaterials = materials;
            }
            vehicles.Add(new Vehicle { root = root, id = vehicles.Count + 1, classId = classId, spawned = spawned, paint = paints.Values.ToArray(), baseScale=root.localScale, vehicleRenderers=root.GetComponentsInChildren<Renderer>(true).Where(IsVehicleRenderer).ToArray() });
        }
        void SetupSurfaces()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TaiwanRider/Editor/VisionInstanceId.shader");
            if (!shader) throw new Exception("Missing instance ID shader");
            idMaterials = new Material[vehicles.Count + 1];
            for (int i = 0; i < idMaterials.Length; i++) {
                idMaterials[i] = new Material(shader);
                idMaterials[i].SetFloat("_InstanceId", i);
            }
            var ids = new Dictionary<Renderer, int>();
            foreach (var v in vehicles)
                foreach (var r in v.vehicleRenderers) ids[r] = v.id;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                int id = ids.TryGetValue(r, out int known) ? known : 0;
                var rgb = r.sharedMaterials;
                if(id==0 && r is MeshRenderer && r.GetComponent<TextMesh>()==null) {
                    for(int i=0;i<rgb.Length;i++)if(rgb[i] && rgb[i].HasProperty("_BaseColor") && rgb[i].shader.name=="Universal Render Pipeline/Lit") {
                        rgb[i]=new Material(rgb[i]);backgrounds.Add(rgb[i]);baseColors.Add(rgb[i].GetColor("_BaseColor"));
                    }
                    r.sharedMaterials=rgb;
                }
                surfaces.Add(new Surface { renderer = r, rgb = rgb, mask = Enumerable.Repeat(idMaterials[id], rgb.Length).ToArray() });
            }
        }
        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);
        void LateUpdate()
        {
            if (!ready) return;
            try {
                Capture();
                index++;
                if (index % 40 == 0) { manifest.Flush(); Debug.Log("VISION_PROGRESS " + index + "/" + (config.train + config.val + config.test)); }
                if (index >= config.train + config.val + config.test) {
                    ready = false; manifest.Close();
                    File.WriteAllText(Path.Combine(config.output, "complete.json"), "{\"frames\":" + index + ",\"utc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
                    Debug.Log("VISION_COMPLETE " + index); EditorApplication.Exit(0);
                }
            } catch (Exception e) {
                ready = false; manifest?.Close();
                File.WriteAllText(Path.Combine(Root, "capture-error.txt"), e.ToString());
                Debug.LogException(e); EditorApplication.Exit(1);
            }
        }
        void Capture()
        {
            string split = index < config.train ? "train" : index < config.train + config.val ? "val" : "test";
            int local = split == "train" ? index : split == "val" ? index - config.train : index - config.train - config.val;
            int seed = config.seed + (split == "train" ? 0 : split == "val" ? 100000 : 200000) + local;
            random = new System.Random(seed);
            float z = split == "train" ? (random.Next(2) == 0 ? Range(-105, -65) : Range(-12, 25)) : split == "val" ? Range(-55, -35) : Range(45, 72);
            float cameraX = Range(-2.8f, 3.3f);
            camera.transform.SetPositionAndRotation(session.route.Position(z, cameraX, Range(1.05f, 2.0f)), session.route.Heading(z) * Quaternion.Euler(Range(-7, 9), Range(-32, 32), Range(-5, 5)));
            camera.fieldOfView = Range(55, 88);
            bool emptyRoad = random.NextDouble() < .22;
            int profile=split=="train"?random.Next(4):split=="val"?4:5;
            int categoryMode=random.Next(5); // one car-only and one motorcycle-only mode
            foreach(var item in clutter) {
                item.gameObject.SetActive(random.NextDouble()<.7);
                item.position=session.route.Position(z+Range(3,38),Range(-4.4f,5.4f),0);
                item.rotation=session.route.Heading(item.position.z)*Quaternion.Euler(0,Range(-180,180),0);
            }
            float tint=Range(.70f,1.15f);
            for(int i=0;i<backgrounds.Count;i++) {
                Color c=baseColors[i];backgrounds[i].SetColor("_BaseColor",new Color(c.r*tint*Range(.9f,1.1f),c.g*tint,c.b*tint*Range(.9f,1.1f),c.a));
            }
            var placed = new List<Vector3>();
            int carN = 0, motoN = 0;
            foreach (var v in vehicles) {
                bool active;
                if (v.spawned) {
                    int ordinal = v.classId == 0 ? carN++ : motoN++;
                    active = random.NextDouble() < (ordinal == 0 ? .88 : .46);
                    if (active) {
                        Vector3 p = Vector3.zero;
                        bool found = false;
                        for (int attempt = 0; attempt < 25; attempt++) {
                            float distance = ordinal == 0 ? Range(3, 28) : Range(6, 55);
                            p = session.route.Position(z + distance, Range(-4.1f, 4.5f), .02f);
                            if (placed.All(q => Vector3.Distance(p, q) > (v.classId == 0 ? 5.5f : 3.5f))) { found = true; break; }
                        }
                        active = found;
                        if (active) {
                            placed.Add(p);
                            float yaw = random.NextDouble() < .25 ? Range(-100, 100) : (random.NextDouble() < .25 ? 180 : 0) + Range(-20, 20);
                            v.root.SetPositionAndRotation(p, session.route.Heading(p.z) * Quaternion.Euler(0, yaw, 0));
                            float width=profile==0?.84f:profile==1?1.12f:profile==2?.98f:profile==3?1.06f:profile==4?.91f:1.17f;
                            float height=profile==0?.9f:profile==1?1.2f:profile==2?1.1f:profile==3?.98f:profile==4?1.25f:1.05f;
                            float length=profile==0?.85f:profile==1?1.12f:profile==2?1.20f:profile==3?.96f:profile==4?1.05f:.91f;
                            v.root.localScale=Vector3.Scale(v.baseScale,new Vector3(width,height,length))*Range(.96f,1.04f);
                        }
                    }
                } else active = random.NextDouble() < .13;
                v.root.gameObject.SetActive(active && !emptyRoad && (categoryMode!=0 || v.classId==0) && (categoryMode!=1 || v.classId==1));
                Color paint = Color.HSVToRGB(Range(0, 1), Range(.02f, .75f), Range(.16f, .9f));
                foreach (var material in v.paint) material.SetColor("_BaseColor", paint);
            }
            int weather = random.Next(7);
            RenderSettings.fog = weather >= 4;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = weather>=4 ? (weather==4?Range(.008f,.018f):weather==5?Range(.02f,.05f):Range(.06f,.12f)) : 0;
            RenderSettings.fogColor = new Color(.57f, .62f, .67f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            float ambient = weather == 3 ? Range(.035f, .14f) : Range(.25f, .7f);
            RenderSettings.ambientLight = new Color(ambient * .94f, ambient, ambient * 1.06f);
            foreach (var sun in suns) {
                sun.intensity = weather == 3 ? Range(.02f, .12f) : Range(.35f, 1.65f);
                sun.color = Color.Lerp(new Color(1f, .83f, .69f), new Color(.88f, .94f, 1), Range(0, 1));
                sun.transform.rotation = Quaternion.Euler(Range(22, 70), Range(-180, 180), 0);
            }
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = RenderSettings.fogColor;
            // The dataset contains RGB-visible targets only. Remove vehicles whose
            // fog transmission is below 25% before BOTH RGB and ID rendering.
            // This avoids training the detector to hallucinate invisible geometry.
            if(RenderSettings.fog)foreach(var v in vehicles) {
                float distance=Vector3.Distance(camera.transform.position,v.root.position);
                float density=RenderSettings.fogDensity;
                if(Mathf.Exp(-density*density*distance*distance)<.25f)v.root.gameObject.SetActive(false);
            }
            Physics.SyncTransforms();
            string stem = split + "_" + local.ToString("D5");
            ReadCamera(rgbTarget, rgbPixels);
            File.WriteAllBytes(Path.Combine(config.output, "images", split, stem + ".jpg"), rgbPixels.EncodeToJPG(94));
            float fog = RenderSettings.fogDensity;
            bool fogOn = RenderSettings.fog;
            RenderSettings.fog = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            foreach (var s in surfaces) if (s.renderer) s.renderer.sharedMaterials = s.mask;
            ReadCamera(maskTarget, maskPixels);
            foreach (var s in surfaces) if (s.renderer) s.renderer.sharedMaterials = s.rgb;
            RenderSettings.fog = fogOn;
            var mask = maskPixels.GetPixels32();
            int count = vehicles.Count + 1;
            var minX = Enumerable.Repeat(Width, count).ToArray(); var minY = Enumerable.Repeat(Height, count).ToArray();
            var maxX = Enumerable.Repeat(-1, count).ToArray(); var maxY = Enumerable.Repeat(-1, count).ToArray();
            var pixels = new int[count];
            for (int offset = 0; offset < mask.Length; offset++) {
                int id = mask[offset].r;
                if (id == 0) continue;
                if (id >= count || mask[offset].g != 0 || mask[offset].b != 0) throw new Exception("Unexpected ID color: " + mask[offset]);
                int x = offset % Width, y = Height - 1 - offset / Width;
                minX[id] = Math.Min(minX[id], x); minY[id] = Math.Min(minY[id], y);
                maxX[id] = Math.Max(maxX[id], x); maxY[id] = Math.Max(maxY[id], y); pixels[id]++;
            }
            var boxes = new List<Box>(); var labels = new List<string>();
            foreach (var v in vehicles) {
                int id = v.id, w = maxX[id] - minX[id] + 1, h = maxY[id] - minY[id] + 1;
                if (pixels[id] < 12 || w < 4 || h < 4) continue;
                boxes.Add(new Box { id = id, classId = v.classId, x = minX[id], y = minY[id], width = w, height = h, pixels = pixels[id], distance = Vector3.Distance(camera.transform.position, v.root.position) });
                labels.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:F7} {2:F7} {3:F7} {4:F7}", v.classId, (minX[id] + w * .5f) / Width, (minY[id] + h * .5f) / Height, w / (float)Width, h / (float)Height));
            }
            File.WriteAllLines(Path.Combine(config.output, "labels", split, stem + ".txt"), labels);
            manifest.WriteLine(JsonUtility.ToJson(new Frame {
                image = "images/" + split + "/" + stem + ".jpg", split = split, index = local, seed = seed,
                cameraPosition = camera.transform.position, cameraYaw = camera.transform.eulerAngles.y,
                fieldOfView = camera.fieldOfView, fogDensity = fog,
                weather = weather>=4 ? "fog_"+weather : weather==3 ? "night" : "daylight", geometryProfile=profile,negative=emptyRoad, boxes = boxes.ToArray()
            }));
            if (local < 6 || local % 100 == 0) File.WriteAllBytes(Path.Combine(config.output, "qa-masks", stem + ".png"), maskPixels.EncodeToPNG());
        }

        static bool IsVehicleRenderer(Renderer r)
        {
            string n=r.name.ToLowerInvariant();
            return !(n.Contains("rider torso") || n.Contains("helmet") || n=="sleeve" || n=="trousers");
        }
        void SetupClutter()
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            for(int i=0;i<16;i++) {
                var group=new GameObject("Training negative clutter "+i).transform;
                clutter.Add(group);
                if(i%4==0) {
                    AddPart(group,PrimitiveType.Capsule,new Vector3(0,.8f,0),new Vector3(.4f,.55f,.3f),new Color(.15f,.22f,.27f));
                    AddPart(group,PrimitiveType.Sphere,new Vector3(0,1.5f,0),Vector3.one*.26f,new Color(.65f,.48f,.36f));
                    for(int sign=-1;sign<=1;sign+=2)AddPart(group,PrimitiveType.Capsule,new Vector3(sign*.11f,.3f,0),new Vector3(.13f,.3f,.13f),new Color(.08f,.1f,.13f));
                } else if(i%4==1) {
                    AddPart(group,PrimitiveType.Cylinder,new Vector3(0,.45f,0),new Vector3(.16f,.45f,.16f),new Color(.85f,.38f,.06f));
                    AddPart(group,PrimitiveType.Cube,new Vector3(0,.04f,0),new Vector3(.45f,.08f,.45f),Color.gray);
                } else if(i%4==2) {
                    AddPart(group,PrimitiveType.Cylinder,new Vector3(0,.8f,0),new Vector3(.06f,.8f,.06f),Color.gray);
                    AddPart(group,PrimitiveType.Cube,new Vector3(0,1.5f,0),new Vector3(.65f,.5f,.08f),new Color(.8f,.7f,.25f));
                } else AddPart(group,PrimitiveType.Cube,new Vector3(0,.45f,0),new Vector3(.8f,.9f,.6f),new Color(.34f,.28f,.2f));
            }
        }
        void AddPart(Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(type);go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=scale;
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.SetColor("_BaseColor",color);go.GetComponent<Renderer>().sharedMaterial=m;
            Destroy(go.GetComponent<Collider>());
        }

        void PrepareFogMaterials()
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/TaiwanRider/Editor/CaptureFogStreetLabel.shader");
            if(!shader)throw new Exception("Missing fog label shader");
            var replacements=new Dictionary<Material,Material>();
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) {
                var materials=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<materials.Length;i++) {
                    var original=materials[i];
                    if(!original || (original.shader.name!="TaiwanRider/StreetLabelDepth" && renderer.GetComponent<TextMesh>()==null))continue;
                    if(!replacements.TryGetValue(original,out var replacement)){replacement=new Material(original){shader=shader};replacements.Add(original,replacement);}
                    materials[i]=replacement;changed=true;
                }
                if(changed)renderer.sharedMaterials=materials;
            }
        }

        void ReadCamera(RenderTexture target, Texture2D pixels)
        {
            cameraData.resetHistory = true;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(camera, request);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            pixels.Apply(false);
            RenderTexture.active = previous;
        }
    }
}
