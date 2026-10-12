using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TaiwanRider
{
    // RGB supplies class/confidence; the existing raycast tracker supplies geometry.
    // No object name, tag, scenario kind, or ground-truth class enters inference.
    [DefaultExecutionOrder(500)]
    public sealed class RiderVisionSensor : MonoBehaviour
    {
        [Serializable] public struct Detection
        {
            public Rect box; // Top-left origin, pixels in the 640 x 360 camera image.
            public int classId;
            public float confidence;
            public string ClassName => classId == 0 ? "car" : "motorcycle";
        }
        public struct Evidence
        {
            public Detection detection;
            public float overlap, capturedAt;
            public double capturedRealtime;
        }
        struct Projection { public Collider collider; public Rect box; }
        struct Pair { public int detection, projection; public float overlap; }
        public ScenarioRunner session;
        public RadarSensor radar;
        public bool FusionEnabled = true;
        [Range(1, 20)] public float inferenceHz = 10;
        public const float MaxEvidenceAge = .35f;
        public const float FusionConfidence = .6f;
        public const int ImageWidth = 640, ImageHeight = 360, InputSize = 640;
        const int PaddingY = 140, Candidates = 8400;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public int CompletedFrames { get; private set; }
        public int MatchedContacts { get; private set; }
        public float LastLatencyMs { get; private set; }
        public double LastCapturedRealtime { get; private set; }
        public IReadOnlyList<Detection> Detections => detections;
        public RenderTexture CameraImage => rgb;
        public RenderTexture ModelImage => letterbox;
        public float[] LastRawOutput { get; private set; }
        public Camera PerceptionCamera => captureCamera;
        // Validation only; this synchronously downloads one tensor, never the control loop.
        public float[] CopyInputForCheck() => input.DownloadToArray();
        readonly List<Detection> detections = new List<Detection>();
        readonly List<Detection> candidates = new List<Detection>();
        readonly List<Projection> projections = new List<Projection>();
        readonly List<Pair> pairs = new List<Pair>();
        readonly Dictionary<Collider, Evidence> evidence = new Dictionary<Collider, Evidence>();
        Worker worker;
        Tensor<float> input, output;
        Camera captureCamera;
        RenderTexture rgb, letterbox;
        Material preprocess;
        bool pending, warming;
        int generation, scheduledGeneration;
        float capturedAt, nextGameTime;
        double capturedRealtime, nextRealtime;

        void Start()
        {
            try
            {
                var asset = Resources.Load<ModelAsset>("RideReflexVision/ridereflex-vision-v1");
                if (!asset) throw new InvalidOperationException("RideReflex vision ONNX is missing from Resources/RideReflexVision.");
                if (!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("GPU compute is unavailable; using raycast fallback.");
                var model = ModelLoader.Load(asset);
                worker = new Worker(model, BackendType.GPUCompute);
                input = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize));
                var go = new GameObject("RideReflex RGB perception camera");
                captureCamera = go.AddComponent<Camera>();
                captureCamera.CopyFrom(session.rider.riderCamera);
                captureCamera.enabled = false;
                captureCamera.aspect = ImageWidth / (float)ImageHeight;
                captureCamera.fieldOfView = 68;
                captureCamera.cullingMask &= ~(1 << 8); // Ego vehicle/avatar only.
                var data = captureCamera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                rgb = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
                rgb.Create();
                // Store gamma-encoded RGB as raw values for the network.
                letterbox = new RenderTexture(InputSize, InputSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                letterbox.Create();
                var shader = Resources.Load<Shader>("RideReflexVision/Letterbox");
                if (!shader) throw new InvalidOperationException("Vision letterbox shader is missing.");
                preprocess = new Material(shader);
                Schedule(true); // Warm the GPU kernels before a run starts.
            }
            catch (Exception ex) { Fail(ex); }
        }

        public void ResetForRun()
        {
            generation++;
            evidence.Clear(); detections.Clear(); MatchedContacts = 0; CompletedFrames = 0;
            nextGameTime = 0; nextRealtime = 0;
            // An in-flight result keeps its generation and is discarded on completion.
        }
        public void SetFusionEnabled(bool value)
        {
            FusionEnabled = value;
            ResetForRun();
        }
        public bool TryGetEvidence(Collider collider, out Evidence value)
        {
            value = default;
            if (!enabled || !FusionEnabled || !Ready || !collider || session.IsPaused) return false;
            if (!evidence.TryGetValue(collider, out value)) return false;
            return Time.time - value.capturedAt <= MaxEvidenceAge &&
                Time.realtimeSinceStartupAsDouble - value.capturedRealtime <= MaxEvidenceAge;
        }
        public float Horizon(Collider collider)
        {
            if (!TryGetEvidence(collider, out var value)) return 1.8f;
            return value.detection.classId == 1 ? 2.4f : 2.2f;
        }
        void LateUpdate()
        {
            if (worker == null || Error != null) return;
            try
            {
                if (pending && output.IsReadbackRequestDone())
                {
                    var values = output.DownloadToArray();
                    pending = false;
                    if (output.shape.rank != 3 || output.shape[0] != 1 || output.shape[1] != 6 || output.shape[2] != Candidates || values.Length < 6 * Candidates)
                        throw new InvalidOperationException("Unexpected YOLO output shape: " + output.shape);
                    // Inference Engine 2.6.1's polled GPU readback may return the
                    // entire pooled buffer, which can exceed the logical tensor.
                    if (values.Length != 6 * Candidates) Array.Resize(ref values, 6 * Candidates);
                    LastLatencyMs = (float)((Time.realtimeSinceStartupAsDouble - capturedRealtime) * 1000);
                    if (warming) { warming = false; Ready = true; }
                    else if (scheduledGeneration == generation && FusionEnabled && session.IsRunning && !session.IsPaused)
                    {
                        LastRawOutput = values;
                        Decode(values); Match(); CompletedFrames++;
                        LastCapturedRealtime = capturedRealtime;
                    }
                    return; // Keep completed input/output paired until the next frame.
                }
                if (!pending && Ready && FusionEnabled && session.IsRunning && !session.IsPaused && !session.rider.HasCrashed &&
                    Time.time >= nextGameTime && Time.realtimeSinceStartupAsDouble >= nextRealtime) Schedule(false);
            }
            catch (Exception ex) { Fail(ex); }
        }
        void Schedule(bool warmup)
        {
            capturedAt = Time.time;
            capturedRealtime = Time.realtimeSinceStartupAsDouble;
            captureCamera.transform.SetPositionAndRotation(session.rider.riderCamera.transform.position, session.rider.riderCamera.transform.rotation);
            var display = session.Demo ? session.Demo.Display : null;
            bool displayActive = display && display.gameObject.activeSelf;
            try
            {
                // Rendered RGB never contains the ray visualization or OnGUI overlay.
                if (displayActive) display.gameObject.SetActive(false);
                RenderPipeline.SubmitRenderRequest(captureCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = rgb });
            }
            finally { if (displayActive) display.gameObject.SetActive(true); }
            Graphics.Blit(rgb, letterbox, preprocess);
            TextureConverter.ToTensor(letterbox, input, new TextureTransform().SetTensorLayout(TensorLayout.NCHW).SetCoordOrigin(CoordOrigin.TopLeft));
            projections.Clear();
            foreach (var contact in radar.Contacts)
                if (contact.collider && Project(new Bounds(contact.position, contact.extents * 2), out var box))
                    projections.Add(new Projection { collider = contact.collider, box = box });
            scheduledGeneration = generation;
            nextGameTime = Time.time + 1f / inferenceHz;
            nextRealtime = capturedRealtime + 1d / inferenceHz;
            worker.Schedule(input);
            output = worker.PeekOutput("output0") as Tensor<float>;
            if (output == null) throw new InvalidOperationException("ONNX output0 is missing.");
            output.ReadbackRequest(); pending = true; warming = warmup;
        }
        bool Project(Bounds bounds, out Rect box)
        {
            float minX = ImageWidth, minY = ImageHeight, maxX = 0, maxY = 0;
            for (int i = 0; i < 8; i++)
            {
                var p = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var v = captureCamera.WorldToViewportPoint(p);
                if (v.z <= captureCamera.nearClipPlane) { box = default; return false; }
                float x = v.x * ImageWidth, y = (1 - v.y) * ImageHeight;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            box = Rect.MinMaxRect(Mathf.Clamp(minX, 0, ImageWidth), Mathf.Clamp(minY, 0, ImageHeight), Mathf.Clamp(maxX, 0, ImageWidth), Mathf.Clamp(maxY, 0, ImageHeight));
            return box.width >= 3 && box.height >= 3;
        }
        public static float IoU(Rect a, Rect b)
        {
            float intersection = Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) *
                Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            return intersection / Mathf.Max(.001f, a.width * a.height + b.width * b.height - intersection);
        }
        void Decode(float[] values)
        {
            candidates.Clear(); detections.Clear();
            for (int i = 0; i < Candidates; i++)
            {
                float car = values[4 * Candidates + i], motorcycle = values[5 * Candidates + i];
                float confidence = Mathf.Max(car, motorcycle);
                if (float.IsNaN(confidence) || confidence < .25f) continue;
                float x = values[i], y = values[Candidates + i] - PaddingY, w = values[2 * Candidates + i], h = values[3 * Candidates + i];
                var box = Rect.MinMaxRect(Mathf.Clamp(x - w / 2, 0, ImageWidth), Mathf.Clamp(y - h / 2, 0, ImageHeight), Mathf.Clamp(x + w / 2, 0, ImageWidth), Mathf.Clamp(y + h / 2, 0, ImageHeight));
                if (box.width < 2 || box.height < 2) continue;
                candidates.Add(new Detection { box = box, confidence = confidence, classId = car >= motorcycle ? 0 : 1 });
            }
            candidates.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            foreach (var candidate in candidates)
            {
                bool suppressed = false;
                foreach (var kept in detections)
                    if (kept.classId == candidate.classId && IoU(kept.box, candidate.box) > .7f) { suppressed = true; break; }
                if (!suppressed) detections.Add(candidate);
                if (detections.Count >= 100) break;
            }
        }
        void Match()
        {
            evidence.Clear(); pairs.Clear();
            // Snapshot projections use the camera pose and radar tracks at image capture,
            // not their later positions when the asynchronous GPU result arrives.
            for (int d = 0; d < detections.Count; d++)
            {
                // A weak detection may be displayed, but must not change policy.
                if (detections[d].confidence < FusionConfidence) continue;
                for (int p = 0; p < projections.Count; p++)
                {
                    float overlap = IoU(detections[d].box, projections[p].box);
                    if (overlap >= .15f && projections[p].box.Contains(detections[d].box.center))
                        pairs.Add(new Pair { detection = d, projection = p, overlap = overlap });
                }
            }
            pairs.Sort((a, b) => b.overlap.CompareTo(a.overlap));
            var used = new HashSet<int>();
            foreach (var pair in pairs)
            {
                var collider = projections[pair.projection].collider;
                if (!collider || used.Contains(pair.detection) || evidence.ContainsKey(collider)) continue;
                // Reject ambiguous adjacent/occluded contacts with similarly good overlap.
                bool ambiguous = false;
                foreach (var other in pairs)
                    if (other.detection == pair.detection && other.projection != pair.projection && other.overlap >= pair.overlap * .9f)
                    { ambiguous = true; break; }
                if (ambiguous) continue;
                used.Add(pair.detection);
                evidence[collider] = new Evidence { detection = detections[pair.detection], overlap = pair.overlap, capturedAt = capturedAt, capturedRealtime = capturedRealtime };
            }
            MatchedContacts = evidence.Count;
        }
        void Fail(Exception ex)
        {
            Error = ex.Message; Ready = false; evidence.Clear();
            Debug.LogWarning("RideReflex vision unavailable; raycast fallback remains active. " + ex);
        }
        void OnDisable() { ResetForRun(); }
        void OnDestroy()
        {
            worker?.Dispose(); input?.Dispose();
            if (rgb) { rgb.Release(); Destroy(rgb); }
            if (letterbox) { letterbox.Release(); Destroy(letterbox); }
            if (preprocess) Destroy(preprocess);
            if (captureCamera) Destroy(captureCamera.gameObject);
        }
    }
}
