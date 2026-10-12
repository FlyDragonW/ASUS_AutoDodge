# RideReflex vision v1

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
7. If matching fails, the model is disabled/unavailable, or evidence is older than 350 ms in either game time or wall time, use the original 1.8-second Raycast policy. Reset and toggle invalidate in-flight results. Unmatched visual detections alone cannot command steering or braking.

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

The [87-second demo](../Validation/Demo/RideReflex-vision-demo.mp4) includes actual model output and a synchronized first-person parked-car comparison. Camera boxes are drawn from recorded detections on the exact inference input; each input is held until the next completed result. The displayed boxes require confidence ≥ 0.60, with at most six sufficiently large boxes shown.

The comparison uses identical initial conditions, a 40 km/h cruise target and the same trim on both videos. Fusion intervened at **7.9333 s**, versus **8.3667 s** for Raycast only: **0.4333 s earlier** (13 capture frames). Both runs finished without a recorded collision. This is a separate presentation capture at fixed `Time.captureFramerate = 30`; it is not the source of the 0.40 s real-time comparison or the 52.9 / 79.4 ms latency measurements above.

- [Raycast capture record](../Validation/VisionV1/video-capture-raycast.json)
- [Fusion capture record](../Validation/VisionV1/video-capture-fusion.json)
- [Chinese voiceover script](DEMO_NARRATION_zh-TW.txt) · [Bilingual subtitles](../Validation/Demo/RideReflex-vision-demo.srt)

All footage hides the game HUD and sensor rays. Titles, subtitles and detection boxes are added in editing. No slide screens or voiceover audio are included; the script is ready for recording. The fog crash illustrates poor visibility and does not demonstrate fog recognition.

## Controls and diagnostics

Press **H** in Play mode to toggle vision fusion. **F2** toggles automatic control, **V** switches the presentation camera, and **G** toggles the ray visualization. The normal demo HUD reports detections, contact matches and end-to-end latency; all captures in the automated checks hide the HUD and rays.

`AutomaticAvoidance.LastDecisionSource` distinguishes:

- `Raycast`: no usable visual evidence for the selected obstacle.
- `Raycast + vision confirmation`: the model recognizes the contact, but the baseline would already react at that instant.
- `Vision + raycast early intervention`: fresh classification extends the horizon and the original policy would not yet react at that instant.

This last label is a local policy comparison. A separate paired run is needed to compare total route outcomes and intervention times; physics scheduling can differ across runs.

## Reproduce the integration check

Run the editor with graphics enabled (do not use `-nographics`):

```powershell
& '<Unity Editor>/Unity.exe' -batchmode -projectPath '<checkout>' `
  -executeMethod TaiwanRider.Editor.VisionIntegrationCheck.Begin `
  -visionOutput '<absolute output directory>' -logFile '<absolute log file>'
```

The check opens the existing scene, enters Play mode, warms the model, runs the scenarios, writes JSON and clean camera frames, and exits Unity. It does not save scene changes. The report records actual wall time with `Time.captureFramerate = 0` and `Time.timeScale = 1`.

GPU output is also compared with ONNX Runtime on an identical saved tensor image. The first cold model invocation is a warmup and is excluded from route latency summaries.

UGen300 / Hailo deployment remains future work. The current runtime uses the host GPU; ONNX export is not an HEF compilation or hardware compatibility test.

## Attribution

The detector is derived from Ultralytics YOLOv8n COCO weights and was trained with Ultralytics 8.3.161. See [third-party notices](../Sources/THIRD_PARTY_NOTICES.txt) and the included upstream AGPL-3.0 license. Training and model export details above describe this checkpoint.
