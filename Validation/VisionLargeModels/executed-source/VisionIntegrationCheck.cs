using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TaiwanRider.Editor
{
    [InitializeOnLoad]
    public static class VisionIntegrationCheck
    {
        const string Key = "RideReflex.VisionIntegrationCheck";
        static VisionIntegrationCheck() { EditorApplication.playModeStateChanged += OnPlay; }
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
            new GameObject("Vision integration checks").AddComponent<VisionIntegrationRecorder>();
        }
    }

    [DefaultExecutionOrder(10000)]
    public sealed class VisionIntegrationRecorder : MonoBehaviour
    {
        [Serializable] public class Intervention
        {
            public float seconds, speedKph, ttc, confidence, evidenceAgeMs;
            public string state, source, visualClass, collider;
            public bool baselineWouldReact;
        }
        [Serializable] public class Frame
        {
            public float seconds, latencyMs;
            public int detections, matches;
            public bool hazardMatched;
            public string hazardClass;
            public float hazardConfidence;
        }
        [Serializable] public class Case
        {
            public string name;
            public bool fusion, disableDuringRun, passed, timedOut;
            public RunResult result;
            public double wallSeconds;
            public int renderedFrames, inferenceFrames, matchedFrames, hazardMatchedFrames, hazardCorrectClassFrames, hazardWrongClassFrames, visionAssistedInterventions, extendedRiskSteps;
            public float meanLatencyMs, p95LatencyMs, maxLatencyMs, firstInterventionAt, eventStartedAt;
            public Intervention[] interventions;
            public Frame[] inference;
        }
        [Serializable] public class Report
        {
            public string utc, unityVersion, gpu, model, backend = "Unity Inference Engine GPUCompute", error;
            public bool passed, gameUiVisible, rayVisualizationVisible;
            public int targetInferenceHz = 10;
            public string timing = "Real elapsed time; captureFramerate=0, timeScale=1. Includes capture/render/readback overhead. Synthetic scene only.";
            public Case[] cases;
        }
        [Serializable] public class DetectionDump { public RiderVisionSensor.Detection[] detections; }
        ScenarioRunner session;
        RiderVisionSensor vision;
        string folder;
        Case current;
        readonly List<Case> cases = new List<Case>();
        readonly List<Frame> frames = new List<Frame>();
        readonly List<Intervention> events = new List<Intervention>();
        int previousCompleted, previousInterventions;
        double started;
        bool exported, shot, disabled, exiting, smoke;
        RenderTexture preview;
        Texture2D pixels;

        IEnumerator Start()
        {
            folder = Path.GetFullPath("Validation/VisionV1/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var args = Environment.GetCommandLineArgs();
            smoke = Array.IndexOf(args, "-visionSmoke") >= 0;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-visionOutput") folder = args[i + 1];
            Directory.CreateDirectory(folder);
            Application.runInBackground = true;
            Time.captureFramerate = 0; Time.timeScale = 1;
            session = FindAnyObjectByType<ScenarioRunner>();
            while (!session || !session.Demo) yield return null;
            session.ShowUI = false; session.SuppressResultFiles = true;
            session.rider.InputOverride = null; session.rider.cameraMotion = false;
            session.SetDemoMode(true);
            session.Demo.SetRadarVisible(false); session.Demo.SetThirdPerson(false);
            session.Demo.Pilot.cruiseSpeedKph = 40;
            vision = session.Demo.Vision;
            double warmStart = Time.realtimeSinceStartupAsDouble;
            while (!vision.Ready && vision.Error == null && Time.realtimeSinceStartupAsDouble - warmStart < 90) yield return null;
            if (!vision.Ready) { End("Model warmup failed: " + vision.Error); yield break; }
            preview = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); preview.Create();
            pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            for (int mode = 0; mode < 9; mode++)
            {
                if (smoke && mode != 1) continue;
                bool parked = mode == 6 || mode == 7;
                int scenario = mode < 6 ? mode / 2 : 1;
                bool fusion = mode % 2 == 1 || mode == 8;
                current = new Case { name = (mode == 8 ? "midrun-disable-car" : (parked ? "parked-car" : scenario == 0 ? "cruise" : scenario == 1 ? "car-cut-in" : "scooter-crossing") + (fusion ? "-fusion" : "-raycast")), fusion = fusion, disableDuringRun = mode == 8 };
                vision.SetFusionEnabled(fusion);
                session.car.enabled = true;
                session.StartRun(scenario);
                if (parked)
                {
                    session.car.enabled = false; session.car.active = false;
                    session.car.transform.SetPositionAndRotation(session.route.Position(0, 2.65f, session.car.transform.position.y), session.route.Heading(0));
                    Physics.SyncTransforms();
                }
                Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
                frames.Clear(); events.Clear(); previousCompleted = previousInterventions = 0;
                shot = disabled = false;
                started = Time.realtimeSinceStartupAsDouble;
                Debug.Log("VISION_CHECK_START " + current.name);
                while (session.IsRunning && session.elapsed < 60 && Time.realtimeSinceStartupAsDouble - started < 150 && vision.Error == null) yield return null;
                current.timedOut = session.IsRunning;
                current.wallSeconds = Time.realtimeSinceStartupAsDouble - started;
                current.result = session.Result;
                current.firstInterventionAt = session.Demo.Pilot.FirstInterventionAt;
                current.eventStartedAt = session.EventStartedAt;
                current.visionAssistedInterventions = session.Demo.Pilot.VisionAssistedInterventions;
                current.extendedRiskSteps = session.Demo.Pilot.VisionExtendedRiskSteps;
                current.interventions = events.ToArray(); current.inference = frames.ToArray();
                current.inferenceFrames = frames.Count;
                var latencies = new List<float>();
                foreach (var frame in frames) { latencies.Add(frame.latencyMs); current.meanLatencyMs += frame.latencyMs; }
                if (latencies.Count > 0) { latencies.Sort(); current.meanLatencyMs /= latencies.Count; current.p95LatencyMs = latencies[Mathf.Min(latencies.Count - 1, Mathf.CeilToInt(latencies.Count * .95f) - 1)]; current.maxLatencyMs = latencies[latencies.Count - 1]; }
                current.passed = !current.timedOut && current.result.finished && !current.result.collision &&
                    (scenario == 0 ? events.Count == 0 : (current.result.hazardTriggered && events.Count > 0)) && (!fusion || (frames.Count > 0 && current.matchedFrames > 0)) && vision.Error == null;
                cases.Add(current);
                File.WriteAllText(Path.Combine(folder, current.name + ".json"), JsonUtility.ToJson(current, true));
                Debug.Log("VISION_CHECK_DONE " + current.name + " passed=" + current.passed + " matched=" + current.hazardMatchedFrames + " early=" + current.visionAssistedInterventions);
                current = null;
                session.ResetToMenu();
                for (int i = 0; i < 10; i++) yield return null;
                if (vision.Error != null) { End(vision.Error); yield break; }
            }
            End(null);
        }
        void LateUpdate()
        {
            if (current == null || exiting) return;
            try
            {
                current.renderedFrames++;
                if (current.disableDuringRun && !disabled && session.elapsed >= 3)
                {
                    vision.SetFusionEnabled(false); disabled = true; previousCompleted = 0;
                }
                if (vision.CompletedFrames != previousCompleted)
                {
                    previousCompleted = vision.CompletedFrames;
                    var actor = session.selectedScenario == 1 ? session.car : session.scooter;
                    bool matched = session.selectedScenario != 0 && vision.TryGetEvidence(actor.bodyCollider, out _);
                    vision.TryGetEvidence(actor.bodyCollider, out var evidence);
                    frames.Add(new Frame { seconds = session.elapsed, latencyMs = vision.LastLatencyMs, detections = vision.Detections.Count, matches = vision.MatchedContacts,
                        hazardMatched = matched, hazardClass = matched ? evidence.detection.ClassName : "", hazardConfidence = matched ? evidence.detection.confidence : 0 });
                    if (vision.MatchedContacts > 0) current.matchedFrames++;
                    if (matched) current.hazardMatchedFrames++;
                    if (matched)
                    {
                        if (evidence.detection.classId == (session.selectedScenario == 2 ? 1 : 0)) current.hazardCorrectClassFrames++;
                        else current.hazardWrongClassFrames++;
                    }
                    if (!exported && session.elapsed > 3)
                    {
                        ExportTensorCheck(); exported = true;
                    }
                    if (!shot && matched && session.EventStartedAt >= 0)
                    {
                        SaveTexture(vision.CameraImage, Path.Combine(folder, current.name + "-camera.png"));
                        SaveTexture(vision.ModelImage, Path.Combine(folder, current.name + "-model.png"));
                        var list = new List<RiderVisionSensor.Detection>(vision.Detections);
                        File.WriteAllText(Path.Combine(folder, current.name + "-detections.json"), JsonUtility.ToJson(new DetectionDump { detections = list.ToArray() }, true));
                        shot = true;
                    }
                }
                var pilot = session.Demo.Pilot;
                if (pilot.InterventionCount != previousInterventions)
                {
                    previousInterventions = pilot.InterventionCount;
                    events.Add(new Intervention { seconds = session.elapsed, speedKph = session.rider.SpeedKph, ttc = pilot.LastInterventionTtc,
                        state = pilot.State.ToString(), source = pilot.LastDecisionSource, visualClass = pilot.LastVisualClass,
                        confidence = pilot.LastVisualConfidence, evidenceAgeMs = pilot.LastVisualAgeMs, baselineWouldReact = pilot.LastBaselineWouldReact,
                        collider = pilot.Threat ? pilot.Threat.name : "" });
                    var cam = session.Demo.ActiveCamera;
                    cam.aspect = 16f / 9; cam.fieldOfView = 68;
                    RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = preview });
                    SaveTexture(preview, Path.Combine(folder, current.name + "-intervention-" + previousInterventions + ".png"));
                }
            }
            catch (Exception ex) { End(ex.ToString()); }
        }
        void ExportTensorCheck()
        {
            SaveTexture(vision.ModelImage, Path.Combine(folder, "parity-input.png"));
            SaveTexture(vision.CameraImage, Path.Combine(folder, "parity-camera.png"));
            var values = vision.LastRawOutput;
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(folder, "parity-unity-output.f32"), bytes);
            var inputs = vision.CopyInputForCheck();
            var inputBytes = new byte[3 * 640 * 640 * sizeof(float)];
            Buffer.BlockCopy(inputs, 0, inputBytes, 0, inputBytes.Length);
            File.WriteAllBytes(Path.Combine(folder, "parity-unity-input.f32"), inputBytes);
        }
        static void SaveTexture(RenderTexture texture, string path)
        {
            var old = RenderTexture.active; RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false, true);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply(false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Destroy(image); RenderTexture.active = old;
        }
        void End(string error)
        {
            if (exiting) return; exiting = true;
            bool passed = error == null && cases.Count == (smoke ? 1 : 9) && cases.TrueForAll(c => c.passed);
            if (!smoke) passed &= cases.Exists(c => c.visionAssistedInterventions > 0);
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                model = vision ? vision.ModelResourcePath : null, passed = passed, error = error, gameUiVisible = session && session.ShowUI, rayVisualizationVisible = session && session.Demo && session.Demo.Display.Visible, cases = cases.ToArray() };
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("VISION_CHECK_COMPLETE passed=" + passed + " error=" + error);
            EditorApplication.Exit(passed ? 0 : 1);
        }
        void OnDestroy()
        {
            if (preview) { preview.Release(); Destroy(preview); }
            if (pixels) Destroy(pixels);
        }
    }
}
