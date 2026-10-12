using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace TaiwanRider.Editor
{
    [InitializeOnLoad]
    public static class VisionFogCapture
    {
        const string Key = "RideReflex.VisionFogCapture";
        static VisionFogCapture() { EditorApplication.playModeStateChanged += OnPlay; }
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
            new GameObject("Vision narrative recorder").AddComponent<VisionFogRecorder>();
        }
    }

    [DefaultExecutionOrder(12000)]
    public sealed class VisionFogRecorder : MonoBehaviour
    {
        static string Folder = Path.GetFullPath("Validation/VisionFog/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        static string Ffmpeg = "ffmpeg";
        const int Width = 1920, Height = 1080, Fps = 30;
        [Serializable] public class VisionFrame
        {
            public int frame, inference;
            public float gameSeconds, latencyMs;
            public RiderVisionSensor.Detection[] detections;
        }
        [Serializable] public class Transition
        {
            public int frame;
            public float gameSeconds, speedKph, brake, riderZ;
            public bool visualWarning;
            public string state, source, visualClass;
            public float confidence, ttc;
        }
        [Serializable] public class Report
        {
            public string capturedAtUtc, name;
            public int width = Width, height = Height, fps = Fps, frames;
            public bool uiVisible, raysVisible, firstPerson, visionEnabled, parkedCar, visionOnly, roadBlocked, passed;
            public string weather = "Unity ExponentialSquared fog; physics rays are unaffected. Not a physical radar-weather test.";
            public float fogDensity, cameraFarClip, perceptionFarClip, maximumCarDisplacement, maxRiderRise;
            public int impactFrame = -1;
            public string firstDecisionSource;
            public float parkedOffset, minimumClearance = 1000, minimumSpeedAfterSixSeconds = 1000, firstVisualWarningAt = -1;
            public int visualWarningEvents, fusionInterventions, backgroundParkedVehiclesHidden;
            public RiderVisionSensor.Detection firstVisualWarningDetection;
            public string timing = "Fixed 30 fps simulation capture for presentation. Paired fixed 22-second recordings. The simple vision-only baseline uses image boxes; both controllers share route following. No metric distance enters the vision-only warning decision.";
            public string overlayProvenance = "Model-view RGB held from the exact inference input; JSON boxes are native Unity model outputs for that same input. No ground-truth boxes.";
            public float firstInterventionAt;
            public int firstInterventionFrame = -1, visionAssistedInterventions;
            public double wallSeconds;
            public RunResult result;
            public Transition[] transitions;
        }
        sealed class Encoder
        {
            public Process process;
            public Stream input;
            public StreamWriter log;
            public Encoder(string path, int width, int height)
            {
                log = new StreamWriter(path + ".log");
                process = new Process { StartInfo = new ProcessStartInfo(Ffmpeg,
                    "-y -hide_banner -loglevel warning -f rawvideo -pixel_format rgb24 -video_size " + width + "x" + height +
                    " -framerate 30 -i pipe:0 -vf vflip -an -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -movflags +faststart \"" + path + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true
                }};
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) { log.WriteLine(e.Data); log.Flush(); } };
                process.Start(); process.BeginErrorReadLine(); input = process.StandardInput.BaseStream;
            }
            public void Write(byte[] bytes) { input.Write(bytes, 0, bytes.Length); }
            public void Close()
            {
                input.Close(); process.WaitForExit(); int code = process.ExitCode;
                process.Dispose(); log.Dispose();
                if (code != 0) throw new Exception("ffmpeg returned " + code);
            }
        }
        ScenarioRunner session;
        RiderVisionSensor vision;
        VisionOnlyWarningBaseline baseline;
        int hiddenParkedVehicles;
        Vector3 parkedPosition, impactPosition;
        RenderTexture target;
        Texture2D pixels, visionPixels;
        Encoder mainEncoder, visionEncoder;
        StreamWriter visionLog;
        Report report;
        readonly List<Transition> transitions = new List<Transition>();
        byte[] lastVisionPixels;
        int frame, previousCompleted, tail, previousInterventions, previousWarningEvents;
        bool previousWarning;
        AvoidanceState previousState;
        bool recording, failed, allPassed = true;
        double wallStarted;

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-fogOutput") Folder = Path.GetFullPath(args[i + 1]);
                if (args[i] == "-ffmpegPath") Ffmpeg = args[i + 1];
            }
            Directory.CreateDirectory(Folder);
            Application.runInBackground = true;
            session = FindAnyObjectByType<ScenarioRunner>();
            while (!session || !session.Demo) yield return null;
            session.ShowUI = false; session.SuppressResultFiles = true;
            // Isolate one physical vehicle, identically in both paired runs.
            // Scene setup does not supply labels or geometry to the visual baseline.
            foreach (var item in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (item.name.StartsWith("Parked scooter ") || item.name == "Parked sedan")
                { item.gameObject.SetActive(false); hiddenParkedVehicles++; }
            session.rider.InputOverride = null; session.rider.cameraMotion = false;
            session.SetDemoMode(true); session.Demo.SetRadarVisible(false);
            session.Demo.Pilot.cruiseSpeedKph = 40;
            vision = session.Demo.Vision;
            baseline = session.rider.gameObject.AddComponent<VisionOnlyWarningBaseline>();
            baseline.session = session; baseline.vision = vision;
            double timeout = Time.realtimeSinceStartupAsDouble + 90;
            while (!vision.Ready && vision.Error == null && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
            if (!vision.Ready) { Fail("Vision warmup failed: " + vision.Error); yield break; }
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            Time.captureFramerate = Fps;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); target.Create();
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            visionPixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
            string[] names = { "18-fog-vision-only", "19-fog-fusion" };
            for (int i = 0; i < names.Length; i++)
            {
                bool parked = true, fusion = i % 2 == 1;
                bool blocked = true;
                baseline.ResetBaseline();
                session.rider.InputOverride = fusion ? null : baseline;
                vision.SetFusionEnabled(true);
                session.car.enabled = true;
                session.Demo.SetThirdPerson(!parked);
                session.Demo.SetRadarVisible(false);
                session.StartRun(1);
                if (parked)
                {
                    session.car.enabled = false; session.car.active = false;
                    session.car.transform.SetPositionAndRotation(session.route.Position(2, 2.65f, session.car.transform.position.y), session.route.Heading(2));
                    var body = session.car.GetComponent<Rigidbody>();
                    body.isKinematic = true; body.position = session.car.transform.position; body.rotation = session.car.transform.rotation;
                    parkedPosition = session.car.transform.position;
                    Physics.SyncTransforms();
                }
                session.Demo.ActiveCamera.aspect = 16f / 9;
                session.Demo.ActiveCamera.fieldOfView = 68;
                ApplyFog();
                frame = tail = previousCompleted = previousInterventions = previousWarningEvents = 0; previousWarning = false;
                previousState = (AvoidanceState)(-1); transitions.Clear();
                lastVisionPixels = new byte[640 * 360 * 3];
                report = new Report { name = names[i], firstPerson = parked, parkedCar = parked, backgroundParkedVehiclesHidden = hiddenParkedVehicles, visionEnabled = true, visionOnly = !fusion, roadBlocked = blocked, parkedOffset = blocked ? 2.65f : 5f, capturedAtUtc = DateTime.UtcNow.ToString("O") };
                report.fogDensity = RenderSettings.fogDensity; report.cameraFarClip = session.Demo.ActiveCamera.farClipPlane;
                report.perceptionFarClip = vision.PerceptionCamera.farClipPlane;
                mainEncoder = new Encoder(Path.Combine(Folder, names[i] + ".mp4"), Width, Height);
                if (true)
                {
                    visionEncoder = new Encoder(Path.Combine(Folder, names[i] + "-model.mp4"), 640, 360);
                    visionLog = new StreamWriter(Path.Combine(Folder, names[i] + "-detections.jsonl"));
                }
                wallStarted = Time.realtimeSinceStartupAsDouble;
                recording = true;
                Debug.Log("VISION_FOG_START " + names[i]);
                while (recording && !failed) yield return null;
                if (failed) yield break;
                CloseEncoders();
                report.frames = frame; report.result = session.Result;
                report.uiVisible = session.ShowUI; report.raysVisible = session.Demo.Display.Visible;
                report.firstInterventionAt = session.Demo.Pilot.FirstInterventionAt;
                report.visionAssistedInterventions = session.Demo.Pilot.VisionAssistedInterventions;
                report.wallSeconds = Time.realtimeSinceStartupAsDouble - wallStarted;
                report.transitions = transitions.ToArray();
                report.visualWarningEvents = baseline.WarningEvents; report.firstVisualWarningAt = baseline.FirstWarningAt;
                report.fusionInterventions = session.Demo.Pilot.InterventionCount;
                report.passed = fusion ? !report.result.collision && report.result.finished && report.fusionInterventions > 0 && report.firstDecisionSource == "Raycast" : report.result.collision;
                report.passed &= report.cameraFarClip == 8 && report.perceptionFarClip == 8 && report.maximumCarDisplacement < .001f;
                allPassed &= report.passed;
                File.WriteAllText(Path.Combine(Folder, report.name + ".json"), JsonUtility.ToJson(report, true));
                Debug.Log("VISION_FOG_DONE " + report.name + " frames=" + frame + " action=" + report.firstInterventionAt + " warning=" + report.visualWarningEvents + " pilot=" + report.fusionInterventions + " passed=" + report.passed + " clearance=" + report.minimumClearance);
                session.rider.InputOverride = null; session.ResetToMenu();
                for (int j = 0; j < 10; j++) yield return null;
            }
            File.WriteAllText(Path.Combine(Folder, "complete.json"), "{\"complete\":true,\"uiVisible\":false,\"slides\":false}");
            Time.captureFramerate = 0;
            EditorApplication.Exit(allPassed ? 0 : 2);
        }
        void LateUpdate()
        {
            if (!recording || failed) return;
            try
            {
                if (session.ShowUI || session.Demo.Display.Visible) throw new Exception("UI or radar rays enabled during clean capture");
                if (vision.Error != null) throw new Exception(vision.Error);
                var pilot = session.Demo.Pilot;
                if (frame % 3 == 0 || pilot.State != previousState || pilot.InterventionCount != previousInterventions || baseline.WarningActive != previousWarning)
                {
                    previousState = pilot.State; previousWarning = baseline.WarningActive;
                    if (pilot.InterventionCount > previousInterventions && report.firstInterventionFrame < 0)
                    { report.firstInterventionFrame = frame; report.firstDecisionSource = pilot.LastDecisionSource; }
                    previousInterventions = pilot.InterventionCount;
                    transitions.Add(new Transition { frame = frame, gameSeconds = session.elapsed, speedKph = session.rider.SpeedKph,
                        brake = report.visionOnly ? baseline.Brake : pilot.Brake, riderZ = session.rider.transform.position.z, visualWarning = baseline.WarningActive,
                        state = report.visionOnly ? (baseline.WarningActive ? "VisualWarningBrake" : "Cruise") : pilot.State.ToString(), source = report.visionOnly ? "Vision-only image-box warning" : pilot.LastDecisionSource, visualClass = report.visionOnly ? baseline.LastWarningDetection.ClassName : pilot.LastVisualClass,
                        confidence = report.visionOnly ? baseline.LastWarningDetection.confidence : pilot.LastVisualConfidence, ttc = float.IsInfinity(pilot.TimeToCollision) ? -1 : pilot.TimeToCollision });
                }
                if (session.IsRunning && session.elapsed > 6 && session.rider.transform.position.z < 10) report.minimumSpeedAfterSixSeconds = Mathf.Min(report.minimumSpeedAfterSixSeconds, session.rider.SpeedKph);
                if (session.rider.HasCrashed && report.impactFrame < 0)
                { report.impactFrame = frame; impactPosition = session.rider.Crash.RiderBody ? session.rider.Crash.RiderBody.position : session.rider.transform.position; }
                if (report.impactFrame >= 0 && session.rider.Crash.RiderBody)
                    report.maxRiderRise = Mathf.Max(report.maxRiderRise, session.rider.Crash.RiderBody.position.y - impactPosition.y);
                report.maximumCarDisplacement = Mathf.Max(report.maximumCarDisplacement, Vector3.Distance(parkedPosition, session.car.transform.position));
                var ego = session.rider.GetComponent<Collider>();
                Vector3 carPoint = session.car.bodyCollider.ClosestPoint(ego.bounds.center);
                report.minimumClearance = Mathf.Min(report.minimumClearance, Vector3.Distance(carPoint, ego.ClosestPoint(carPoint)));
                if (baseline.WarningEvents > previousWarningEvents)
                {
                    if (previousWarningEvents == 0) report.firstVisualWarningDetection = baseline.LastWarningDetection;
                    previousWarningEvents = baseline.WarningEvents;
                }
                if (visionEncoder != null)
                {
                    if (vision.CompletedFrames != previousCompleted)
                    {
                        previousCompleted = vision.CompletedFrames;
                        var old = RenderTexture.active; RenderTexture.active = vision.CameraImage;
                        visionPixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0, false); visionPixels.Apply(false);
                        RenderTexture.active = old;
                        lastVisionPixels = visionPixels.GetRawTextureData();
                        var snapshot = new VisionFrame { frame = frame, gameSeconds = session.elapsed, inference = previousCompleted,
                            latencyMs = vision.LastLatencyMs, detections = new List<RiderVisionSensor.Detection>(vision.Detections).ToArray() };
                        visionLog.WriteLine(JsonUtility.ToJson(snapshot)); visionLog.Flush();
                    }
                    visionEncoder.Write(lastVisionPixels);
                }
                var oldTarget = RenderTexture.active;
                var cam = session.Demo.ActiveCamera; cam.aspect = 16f / 9;
                RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); pixels.Apply(false);
                RenderTexture.active = oldTarget;
                mainEncoder.Write(pixels.GetRawTextureData());
                frame++;
                if (frame >= Fps * 22) recording = false;
            }
            catch (Exception ex) { Fail(ex.ToString()); }
        }
        void ApplyFog()
        {
            var labelShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TaiwanRider/Editor/CaptureFogStreetLabel.shader");
            if (!labelShader) throw new Exception("Fog label shader is missing");
            var fogMaterials = new Dictionary<Material, Material>();
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < materials.Length; index++) {
                    var original = materials[index];
                    if (!original || (original.shader.name != "TaiwanRider/StreetLabelDepth" && renderer.GetComponent<TextMesh>() == null)) continue;
                    if (!fogMaterials.TryGetValue(original, out var replacement)) {
                        replacement = new Material(original) { shader = labelShader };
                        fogMaterials.Add(original, replacement);
                    }
                    materials[index] = replacement;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .45f;
            RenderSettings.fogColor = new Color(.44f, .48f, .50f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.43f, .46f, .48f);
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) {
                    light.intensity = .65f;
                    light.color = new Color(.82f, .88f, .94f);
                    light.shadowStrength = .3f;
                }
            // Fog and the horizon share a color; there is no clear blue sky behind the mist.
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor;
                camera.farClipPlane = 8f;
            }
        }
        void CloseEncoders()
        {
            mainEncoder?.Close(); mainEncoder = null;
            visionEncoder?.Close(); visionEncoder = null;
            visionLog?.Dispose(); visionLog = null;
        }
        void Fail(string message)
        {
            failed = true; recording = false;
            try { CloseEncoders(); } catch { }
            File.WriteAllText(Path.Combine(Folder, "error.txt"), message);
            Debug.LogError(message); Time.captureFramerate = 0; EditorApplication.Exit(1);
        }
        void OnDestroy()
        {
            if (target) { target.Release(); Destroy(target); }
            if (pixels) Destroy(pixels);
            if (visionPixels) Destroy(visionPixels);
        }
    }
}
