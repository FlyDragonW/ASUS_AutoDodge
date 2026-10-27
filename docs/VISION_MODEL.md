# RideReflex vision

## Current default: v2 — 2026-10-13

Unity now loads **ridereflex-vision-v2.onnx** by default. Use `-visionModel v1`, `-visionModel v2`, `-visionModel coco-l` or `-visionModel v4l` on the Unity Editor/player command line to select the checkpoint; reports record the actual `modelResourcePath`. All four assets remain available. The input, preprocessing, two-class output, association thresholds and fusion policy below are unchanged.

v2 uses YOLOv8n initialized from COCO, fine-tuned for 20 epochs on 4,000 Unity images, with 500 validation and 500 test images. The first 10 modules are frozen. The 80-class training head is retained during fine-tuning; the deployed ONNX gathers box coordinates plus car/motorcycle channels into `[1,6,8400]`. This is not a new learned driving policy.

On the same 500 synthetic test images, mAP50 is 66.14% for v1 and 76.43% for v2. On a fixed 406-image ACDC car/motorcycle development subset, it is 6.81% and 22.76%. At confidence 0.60 on the synthetic test set, FP falls from 441 to 9 while FN increases from 422 to 505. These detection results do not establish a reduction in collisions.

Synthetic splits still share the base street and vehicle meshes. ACDC is a development evaluation set, not a blind final benchmark.

v2 ONNX SHA-256: `d0cd520e2b52d2518735eb3d5a04939a27c0777788bae7d8ad2ea9b37b70d219`.

[Earlier v1/v2 closed-loop comparison and reproduction commands](../Validation/VisionV2/RESULTS.md) use the same repaired Raycast controller and 48 matched conditions per model, plus stop-hold/release tests. Existing demo media and the historical measurements below were recorded with v1.

## Fixed physics timing and dynamic controller — 2026-10-14

The default detector remains v2. Scenario triggers and automatic actuator smoothing now share the 50 Hz physics timeline. The benchmark disables Rigidbody interpolation and records trigger ticks and positions.

Moving contacts use bounded measured-acceleration prediction and repeated bicycle-model rollouts of straight braking, left/right braking, and maintaining cruise only when a predicted path is clear. The planner uses the existing chassis dimensions plus a 15 cm planning margin; physical colliders, vehicle braking power, steering limits, Raycast beams and detector thresholds are unchanged. The static blocked-corridor stop-hold policy remains in place.

This is a controller update, not detector retraining. The known route and collider geometry still come from Unity. Aggressive successful maneuvers can exceed realistic motorcycle stability limits because the current vehicle simulation lacks tire-friction and lean constraints. [Paired baseline / updated results, repeated and held-out cases, failures and source hashes](../Validation/DynamicAvoidance/RESULTS.md) supersede the historical controller measurements below. Demo media has not been rerecorded for this controller.

## Larger model candidates integrated — 2026-10-14

COCO l and v4l both run directly in Unity Inference Engine GPUCompute with the same FP32 `[1,3,640,640]` input and `[1,6,8400]` output. No external Python service is required for gameplay. The runtime default remains v2.

- **COCO l:** original Ultralytics YOLOv8l COCO weights; exported 80-class ONNX followed by Gather `[0,1,2,3,6,7]` to retain boxes, car and motorcycle scores. No additional fine-tuning.
- **v4l:** starts from those same COCO l weights; 3,400 synthetic training images (2,400 new + 1,000 from v2 train), 800 synthetic validation images, 4 epochs. Only 1,542 vehicle classifier parameters are updated, with a fixed COCO teacher; features, box regression, BN moments and other class rows are frozen. Epoch 4 was selected on synthetic validation.
- Added optimizer images are all from Unity; COCO pretraining itself includes real photos. ACDC is an already-used development evaluation, not a blind test.
- Both large models are about 175 MB each. ONNX files use the repository’s existing Git LFS rule; all Resources assets are included in normal builds.

| Model | ONNX SHA-256 |
|---|---|
| coco-l | `d88d23d64e8e233e5f03ce4cb43b6d2a7d8e06fdd3dd7428dce27a764d08d4a6` |
| v4l | `c577e094acac900934b9ab361ab49db6cb4bbca0370cd19d4cacad5969feaa39` |

ACDC car/motorcycle mAP50: v2 **22.76%**, COCO l **53.04%**, v4l **52.46%**. On the same 500-image synthetic test set: **76.43%**, **61.05%**, **66.04%**. Game adaptation improves the l model on this synthetic test but reduces its ACDC result by 0.58 percentage points.

[Fresh three-model closed-loop benchmark](../Validation/VisionLargeModels/RESULTS.md): 48 identical conditions per model, plus 4 hold/release checks each. Model loading and same-input Unity GPU / CPU ORT parity passed for all three. The report separates matched versus fresh evidence, latency, actual inference rate, collisions and unnecessary interventions.

Reproduce a single model with `-executeMethod TaiwanRider.Editor.AvoidanceBenchmark.Begin -visionModel coco-l -benchmarkConfig <absolute-config.json> -benchmarkOutput <new-output-directory>` using `-batchmode` with graphics enabled. Replace `coco-l` with `v2` or `v4l`; thresholds and scenarios are identical.

## Historical v1 model

## Model

- YOLOv8n, fine-tuned from COCO weights with Ultralytics 8.3.161, 40 epochs; best validation checkpoint at epoch 36.
- Classes: `0 = car`, `1 = motorcycle`.
- ONNX SHA-256: `989305d3698e592074fb86c1100f2dbbcaf9283509f516e2fb6bf78c3fe53cd3`.
- Input: `images`, FP32 `[1,3,640,640]`, RGB / 255.
- Output: `output0`, FP32 `[1,6,8400]`: center x/y, width/height, car score, motorcycle score. No objectness channel, no embedded NMS.
- Opset 11; static batch size 1; Unity Inference Engine 2.6.1, GPUCompute.
- The ONNX is in `Assets/TaiwanRider/Resources/RideReflexVision/` and loaded at runtime. No Python service is required.

## Training scope

1,600 synthetic Unity RGB images: 1,200 training, 200 validation, 200 test. Splits use separate camera regions and layout seeds, but share street scenery and vehicle meshes. Variations include pose, color, illumination and light haze. Dense fog and real camera images are not covered by the evaluation.

Held-out test mAP@50: **84.2%**; mAP@50–95: **65.6%**. These are object detection metrics, not avoidance success rates or independent-world generalization results. The checkpoint does not estimate distance, predict control inputs, or identify drivable space.

## Unity processing

1. A dedicated first-person camera follows the rider camera pose, independently of the presentation camera. It renders 640 × 360 RGB with vertical FOV 68°. Ego geometry, radar visualization and game UI are excluded.
2. The letterbox shader pads 140 pixels above and below, with RGB 114. In a linear-color Unity project it converts sampled linear RGB back to gamma-encoded RGB. TextureConverter supplies a top-left-origin NCHW tensor.
3. Schedule at most 10 requests per second, with one inference in flight. GPU readback is polled without waiting for completion on the main thread. Rendering and scheduling still have a cost; measured end-to-end latency includes them.
4. Decode scores ≥ 0.25, apply same-class NMS at IoU 0.7, retain at most 100 boxes.
5. Only detections with confidence ≥ 0.60 may affect control. Project existing raycast contact bounds into the camera at capture time. Match detections one-to-one by overlap (IoU ≥ 0.15 and detected box center inside projected bounds). Reject similarly good competing matches. This is a simple association heuristic, not learned multi-object tracking.
6. A fresh matched car extends the collision prediction horizon from 1.8 to 2.2 seconds; a motorcycle extends it to 2.4 seconds. These are manually chosen policy parameters. They do not change steering geometry or remove unmatched obstacles.
7. If matching fails, the model is disabled/unavailable, or evidence is older than 350 ms in either game time or wall time, use the base 1.8-second Raycast horizon. Reset and toggle invalidate in-flight results. Unmatched visual detections alone cannot command steering or braking.

The visual class comes only from RGB inference. Distance, velocity and object size still come from the simulated raycast/collider tracker. Exact collider bounds and the known route are simulator privileges; this is not a physical radar implementation.

## Integration results — 2026-10-12

Tested in Unity 6000.4.10f1 on **NVIDIA GeForce RTX 3060 Laptop GPU**. Nine route runs completed without a recorded collision: four Raycast / fusion pairs plus a run that disabled vision at 3 seconds. The two clear-road runs had zero interventions.

| Scenario, 40 km/h target | Raycast only | Vision + Raycast | Fusion decision |
|---|---|---|---|
| Clear road | Finish / no collision | Finish / no collision | No intervention |
| Car cut-in | Finish / no collision | Finish / no collision | Raycast + vision confirmation |
| Crossing scooter | Finish / no collision | Finish / no collision | Raycast + vision confirmation |
| Parked car in lane | Finish / no collision | Finish / no collision | Vision + raycast early intervention |

For the parked car, the paired fusion run intervened **0.40 seconds earlier** (7.95 s versus 8.35 s from the start). At that instant the predicted TTC was **2.19 s**, the visual class was `car` with confidence **0.773**, and the original 1.8-second policy would not yet react. This change comes from the manually configured class-dependent horizon, not a learned driving policy. Both versions already avoided this obstacle successfully.

For car cut-in and scooter crossing, vision confirmed the class but did not advance the initial intervention beyond the baseline threshold. These hazards appeared with approximately one second of TTC. Do not attribute their existing successful avoidance solely to AI.

Across the four full fusion runs, mean capture-to-result latency was **52.9 ms**, P95 **79.4 ms**, over 647 completed inferences. This includes rendering, scheduling, GPU execution and readback polling; it is not isolated neural-network compute time. The target is 10 Hz, and actual throughput is recorded per case. Warmup is excluded. The editor also renders the game, and captures a small number of diagnostic images.

Unity GPU output matched CPU ONNX Runtime on one identical saved input: max class-score difference **0.00000226**, and max box-coordinate difference **0.000061 pixels** for candidates above the detection threshold. The tensor values also matched the exported image exactly. This is an integration check, not a full model-accuracy evaluation.

- [Raw scenario report](../Validation/VisionV1/report.json)
- [Numerical parity check](../Validation/VisionV1/parity-check.json)
- [First-person car cut-in capture, no UI](../Validation/VisionV1/car-cut-in-fusion-intervention-1.png)
- [Parked-car perception input, no UI](../Validation/VisionV1/parked-car-fusion-camera.png)

An initial diagnostic run exposed a weak, 0.289-confidence incorrect class association. The final policy requires confidence ≥ 0.60 before classification can affect control. This threshold was selected during integration, not calibrated on an independent control evaluation set. These fixed scenarios do not establish a general collision rate, false-positive rate, or dense-fog capability.

## Demo capture

The [2-minute demo](../Validation/Demo/RideReflex-vision-demo.mp4) introduces the Gongguan / Tingzhou Road Section 3 digital twin, shows collisions with avoidance disabled, and explains our motivation to develop automatic avoidance assistance for scooters. It then follows **trained vision model → Raycast distance sensing → false-alarm comparison → dense-fog fallback → UGen300 plans**. At **44–66 seconds**, the Raycast walkthrough displays actual sensor beams. The game HUD remains hidden throughout. No slide screens or voiceover audio are included; [Chinese narration](DEMO_NARRATION_zh-TW.txt) and [bilingual subtitles](../Validation/Demo/RideReflex-vision-demo.srt) are provided.

The opening collision clips illustrate the problem with automatic avoidance disabled; they are separate from the later vision-only baseline tests. The first 13 seconds use one continuous take, followed by short dissolves between chapters.

Camera detection boxes are drawn from recorded model outputs on the exact inference input, held until the next completed result. The displayed boxes require confidence ≥ 0.60, with at most six sufficiently large boxes shown.

### Vision-only warning versus vision + Raycast

The synchronized segment at **66–79 seconds** compares a **simple image-box warning baseline** with the existing fusion controller. Both share the same known-route follower and a 40 km/h cruise target. The visual baseline consumes only RGB detections for warning decisions; it does not read metric distance, Raycast contacts, collider bounds or scenario labels. It is an illustrative hand-written policy, not a trained driving model or a representative benchmark of all monocular systems.

[VisionOnlyWarningBaseline.cs](../Assets/TaiwanRider/Runtime/VisionOnlyWarningBaseline.cs) triggers braking after two consecutive observations of the same class with IoU ≥ 0.25, confidence ≥ 0.60, box center in the central 20% of the image, box height ≥ 7% of image height and box bottom below the image midpoint. Evidence and the warning hold expire after 0.35 seconds. These thresholds were chosen while developing this demonstration, not calibrated on an independent test set.

| Controlled scenario | Image-box baseline | Vision + Raycast |
|---|---|---|
| Car beside the route, lateral offset 5.0 m | **1 unnecessary warning**, no collision | **0 interventions**, completed, no collision |
| Car blocking the route, lateral offset 2.65 m | Braked; no collision | Avoided the car; completed, no collision |

For the roadside pair, the visual warning began at **9.07 s**. Over the following four seconds, minimum speed was **18.1 km/h** with the visual baseline and **40.0 km/h** with fusion. Fusion passed with **1.28 m** minimum collider clearance. Here the car classification is correct; the false positive is the decision to treat a non-blocking roadside vehicle as a hazard. Raycast geometry supplies the missing clearance / path information. This illustrates the value of adding geometry to this specific image-space rule; it does not show that fusion outperforms Raycast-only control.

This is a controlled single-vehicle experiment: 45 decorative parked vehicles are hidden identically in both runs, while the test car and street remain. The paired clips start from the same state and use identical trims, with no retiming. Each full run records 22 seconds at `Time.captureFramerate = 30`. These captures are separate from the real-time latency and earlier parked-car timing measurements above. The [raw comparison report](../Validation/VisionV1/false-positive-comparison.json) contains setup, thresholds, per-frame telemetry, the recorded scene SHA-256 and all four outcomes. Identical visual reproduction requires the same scene and assets.

To record the check with FFmpeg available:

```powershell
& '<Unity Editor>/Unity.exe' -batchmode -projectPath '<checkout>' `
  -executeMethod TaiwanRider.Editor.VisionFalsePositiveCapture.Begin -visionModel v1 `
  -falsePositiveOutput '<absolute output directory>' -ffmpegPath '<ffmpeg.exe>' `
  -logFile '<absolute log file>'
```

The comparison baseline is added only by this check; ordinary gameplay continues to use the existing fusion controller.

### Dense-fog fallback

At **78–99 seconds**, two first-person recordings approach the same stationary car blocking the lane. Both have live model inference enabled, the same 40 km/h target, Unity `ExponentialSquared` fog density **0.45**, and **8 m far clipping on both the gameplay and perception cameras**. The same 45 decorative parked vehicles are hidden in both runs. The visual baseline and its thresholds are unchanged from the roadside-car comparison. During the fusion shot (**86.8–98.8 s**), live Raycast beams are visible while the game HUD stays hidden. The dedicated perception camera still excludes the ray visualization, so it receives only the foggy RGB scene.

| Controller | Observed outcome |
|---|---|
| Simple vision-only image-box warning | No warning triggered; collision at about **10.3 s** from run start. |
| Vision + Raycast | First intervention at **8.53 s**, decision source **Raycast**, with no visual confirmation. Finished without collision; minimum collider clearance **0.92 m**. |

This is a demonstration of the existing fallback, not successful camera recognition through fog. Unity physics rays are unaffected by visual fog and camera clipping. One controlled pair does not establish a general fog capability, and physical radar / distance-sensor performance in bad weather has not been tested. Capture uses fixed 30 fps and is separate from the real-time latency measurements. [Raw outcomes, inference summary and capture settings](../Validation/VisionV1/fog-comparison.json) preserve the evidence.

Reproduce the fog pair with the same scene and assets:

```powershell
& '<Unity Editor>/Unity.exe' -batchmode -projectPath '<checkout>' `
  -executeMethod TaiwanRider.Editor.VisionFogCapture.Begin -visionModel v1 `
  -fogOutput '<absolute output directory>' -fogShowRays true `
  -ffmpegPath '<ffmpeg.exe>' `
  -logFile '<absolute log file>'
```

The capture helper applies weather only in Play mode and does not save scene changes. Its fog-aware label shader prevents distant street signs from remaining visible through the mist.

## Controls and diagnostics

Press **H** in Play mode to toggle vision fusion. **F2** toggles automatic control, **V** switches the presentation camera, and **G** toggles the ray visualization. The normal demo HUD reports detections, contact matches and end-to-end latency; the original integration checks hide the HUD and rays. The narrated demo intentionally shows rays during its Raycast walkthrough.

`AutomaticAvoidance.LastDecisionSource` distinguishes:

- `Raycast`: no usable visual evidence for the selected obstacle.
- `Raycast + vision confirmation`: the model recognizes the contact, but the baseline would already react at that instant.
- `Vision + raycast early intervention`: fresh classification extends the horizon and the original policy would not yet react at that instant.

This last label is a local policy comparison. A separate paired run is needed to compare total route outcomes and intervention times; physics scheduling can differ across runs.

## Reproduce the integration check

Run the editor with graphics enabled (do not use `-nographics`):

```powershell
& '<Unity Editor>/Unity.exe' -batchmode -projectPath '<checkout>' `
  -executeMethod TaiwanRider.Editor.VisionIntegrationCheck.Begin -visionModel v1 `
  -visionOutput '<absolute output directory>' -logFile '<absolute log file>'
```

The check opens the existing scene, enters Play mode, warms the model, runs the scenarios, writes JSON and clean camera frames, and exits Unity. It does not save scene changes. The report records actual wall time with `Time.captureFramerate = 0` and `Time.timeScale = 1`.

GPU output is also compared with ONNX Runtime on an identical saved tensor image. The first cold model invocation is a warmup and is excluded from route latency summaries.

UGen300 / Hailo deployment remains future work. The current runtime uses the host GPU; ONNX export is not an HEF compilation or hardware compatibility test.

## Attribution

The detector is derived from Ultralytics YOLOv8n COCO weights and was trained with Ultralytics 8.3.161. See [third-party notices](../Sources/THIRD_PARTY_NOTICES.txt) and the included upstream AGPL-3.0 license. Training and model export details above describe this checkpoint.
