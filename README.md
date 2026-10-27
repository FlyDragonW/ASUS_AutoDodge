# RideReflex

**Camera and distance-sensing fusion for scooter hazard avoidance.** Proposed entry for the [ASUS UGen AI League](https://contest.bhuntr.com/en/39jg9vimiynrhlksze/home/), **Everyday AI / Battlefield Lightning**.

Our Unity digital-twin prototype recreates **Section 3 of Tingzhou Road in Gongguan, Taipei**, providing synthetic training images and repeatable avoidance tests. Sudden cut-ins, vehicles stopped in the lane and poor visibility can leave scooter riders little time to react. We are developing RideReflex to explore automatic braking and avoidance assistance for these situations, combining camera perception with distance sensing.

We have completed a working pipeline: **generate synthetic training images in Unity → train a car / motorcycle detector → combine its predictions with simulated distance sensing → run the avoidance controller**. Vision currently runs on the host GPU. The next milestone is deployment and measurement on ASUS UGen300.

## Watch the demo

This video and the screenshots below were recorded with **v1**. The current default is **v2 with the updated dynamic avoidance controller**; see the [fixed-timing controller comparison](Validation/DynamicAvoidance/RESULTS.md). COCO l and v4l remain selectable.

[Watch the 2-minute demo — MP4, 1080p](Validation/Demo/RideReflex-Demo-v10-1080p.mp4) · [720p compact version](Validation/Demo/RideReflex-vision-demo.mp4) · [Chinese voiceover script](docs/DEMO_NARRATION_zh-TW.txt) · [Bilingual subtitles](Validation/Demo/RideReflex-vision-demo.srt)

The video follows **digital twin → unassisted collisions and our motivation → trained vision model → Raycast distance sensing → fewer false alarms → dense-fog fallback → UGen300 plans**. It has Chinese / English subtitles, with **no in-game UI or slide screens**. Detection boxes come from actual model outputs. The soundtrack is left open for voiceover.

| Time | What to watch |
|---|---|
| 0:00–0:08 | Gongguan / Tingzhou Road Section 3 digital twin for training and testing |
| 0:08–0:19 | Sudden cut-in and a blocked lane in poor visibility, with avoidance disabled |
| 0:19–0:31 | Why we want to develop automatic avoidance assistance for scooters |
| 0:31–0:44 | Trained car / motorcycle detector and its actual predictions |
| 0:44–1:06 | Live Raycast beams, distance sensing and rule-based avoidance |
| 1:06–1:19 | Roadside car: simple vision-only warning versus vision + Raycast |
| 1:19–1:39 | Dense fog: visual warning misses the obstacle; fallback avoids it with live Raycast beams shown |
| 1:39–2:00 | Planned UGen300 deployment, measurement and broader data |

![Actual car / motorcycle model outputs captured during a Unity run](Validation/Demo/RideReflex-vision-preview.webp)

The fog comparison uses the same simulated visibility in both runs. The fusion segment displays live Raycast beams; these visualization rays are excluded from the model camera input. Raycast geometry remains available when the model provides no reliable visual evidence. This demonstrates fallback in Unity; real sensor behavior in fog has not been tested.

### Demo screenshots (v1)

| Camera perception | First-person avoidance |
|---|---|
| ![Actual model output: a car detected at 0.95 confidence, with no game UI](Validation/Demo/RideReflex-vision-detection.png) | ![First-person view while steering around a parked car, with no game UI or sensor rays](Validation/Demo/RideReflex-first-person.png) |
| A recorded YOLOv8n prediction on the exact camera input. | Vision + Raycast run, passing a car stopped in the lane. |

Both images are frames from the v1 demo capture. The detection box is added from recorded model output; the first-person image is a clean camera frame.

## What works today

| Component | Implemented |
|---|---|
| Training pipeline | v2: 5,000 synthetic images; 4,000 train / 500 validation / 500 test. YOLOv8n fine-tuned for cars and motorcycles. |
| [Camera perception](Assets/TaiwanRider/Runtime/RiderVisionSensor.cs) | ONNX inference inside Unity Inference Engine, using a dedicated first-person camera and the host GPU. |
| [Distance sensing](Assets/TaiwanRider/Runtime/RadarSensor.cs) | 68 Raycasts simulate surrounding distance sensing; repeated contacts estimate obstacle motion. |
| Sensor association | Model boxes and classes are matched to projected Raycast contacts. Confidence and freshness checks decide whether visual evidence may affect control. |
| [Avoidance](Assets/TaiwanRider/Runtime/AutomaticAvoidance.cs) | A rule-based controller repeatedly compares braking and steering trajectories for moving traffic. Static obstacles use side-clearance checks; if both sides are blocked, it holds the stop until the forward corridor clears. |
| Fallback | Missing, ambiguous, low-confidence or stale visual evidence leaves the Raycast policy available. |

```text
First-person RGB → YOLOv8 (n / l) → class / confidence / image box
Raycast contacts → distance / motion / bounds
                  ↓ association
       rule-based risk prediction → brake / steering / throttle
```

The sensor casts **44 surrounding rays and 24 forward rays**, reaching up to **32 m ahead, 16 m sideways and 10 m behind**. Consecutive hits estimate relative motion; the controller predicts overlap with obstacle bounds, then checks left / right route offsets for clearance. Moving traffic is checked with short-horizon vehicle rollouts, refreshed about every 40 ms while risk persists. The controller prefers a clear braking path, considers maintaining cruise only when a complete predicted path is clear, and brakes when none is clear. The rider returns to the route after the conflict passes.

Visual classification can extend the prediction horizon from **1.8 s** to **2.2 s for cars** or **2.4 s for motorcycles**. These are manually configured control rules. The learned model supplies perception; distance, steering and braking use the simulator's geometry and controller.

## Evidence so far

### Fixed-timing v2 controller comparison — 2026-10-14

Both controllers were rerun with **the same v2 detector and fixed physics-step hazard triggers**. The updated controller uses measured motion trends and repeated braking / steering trajectory checks.

| Check | Previous controller | Updated controller |
|---|---|---|
| Main suite / 48 | 30 completed / 8 stopped / 10 collisions | 36 completed / 8 stopped / 4 collisions |
| Unnecessary interventions / 16 | 0 | 0 |
| Dynamic repeat / 16, collisions | 10 | 6 |
| Held-out simulation conditions / 16, collisions | 2 | 2 |

The detector weights are unchanged; these are controller results. Remaining collisions, repeat variability and steering intensity are reported alongside successes. The simplified vehicle model does not enforce real motorcycle stability limits. [Full comparison, stop-hold checks and reproduction](Validation/DynamicAvoidance/RESULTS.md).

### Earlier three-model comparison — 2026-10-14

v2, COCO l and v4l are selectable with `-visionModel v2`, `-visionModel coco-l` and `-visionModel v4l`. **v2 remains the default.** All three were rerun with the same repaired Raycast controller and 48 matched conditions.

| Model | Completed / stopped / collisions | Unnecessary interventions | Mean capture-to-result latency |
|---|---|---|---|
| v2 (n) | 30 / 8 / 10 | 0 / 16 | 40.1 ms |
| COCO l | 31 / 8 / 9 | 0 / 16 | 179.3 ms |
| v4l | 30 / 8 / 10 | 0 / 16 | 219.6 ms |

The one changed outcome (COCO l, foggy crossing at 20 km/h) used Raycast for initial braking, with zero vision-extended risk steps in both runs. It is not evidence of improved visual avoidance. GPU thermal throttling was recorded during the large-model runs, so latency differences include thermal state and run order.

Additional stop-hold / release checks passed: 12 / 12. Latencies include rendering, scheduling and readback on an RTX 3060 Laptop; these sequential real-time runs are not a UGen300 hardware measurement or statistical safety result.

On the previously evaluated 406-image ACDC development subset, two-class mAP50 is **22.76% / 53.04% / 52.46%** for v2 / COCO l / v4l. v4l uses additional Unity-only training data, but is 0.58 percentage points below its same-capacity COCO initialization on ACDC. Higher offline AP alone does not establish better avoidance. [Full results, freshness, latency, per-scenario outcomes and commands](Validation/VisionLargeModels/RESULTS.md).

### Earlier v1/v2 comparison — 2026-10-13

Both models were rerun with the **same repaired Raycast controller**, thresholds and 48 scenario conditions per model.

| Check | v1 | v2 |
|---|---|---|
| Closed-loop outcomes / 48 | 30 completed / 8 stopped / 10 collisions | 30 completed / 8 stopped / 10 collisions |
| Unnecessary interventions on clear-road / roadside-car cases | 0 / 16 | 0 / 16 |
| Detection mAP50, same 500 synthetic test images | 66.14% | 76.43% |
| Detection mAP50, 406-image ACDC development subset | 6.81% | 22.76% |

The additional 8 stop-hold / release checks all passed. At confidence 0.60 on the synthetic test set, v2 reduces false detections but increases missed detections (FP 441 → 9; FN 422 → 505). Better detection scores have not established better avoidance safety: late dynamic conflicts remain a controller limitation. Single real-time runs per condition are not a real-road collision rate. [Full results, per-scenario breakdown and reproduction](Validation/VisionV2/RESULTS.md).

### Earlier v1 integration and demo examples

| Check | Result | Scope |
|---|---|---|
| Detection | **84.2% mAP@50** | 200 synthetic test images; the splits share vehicle meshes and street scenery. |
| Closed-loop runs | **9/9 routes completed without a recorded collision** | Four Raycast / fusion pairs and one mid-run vision-disable test, at a 40 km/h cruise target. Each case was run once in the final check. |
| Roadside-car example | **1 unnecessary vision-only warning / 0 fusion interventions** | One controlled pair using a simple image-box warning baseline; both runs passed the roadside car without collision. |
| Dense-fog example | **Visual baseline collided / fusion completed without collision** | One controlled pair with identical fog; fusion intervened using Raycast, without visual confirmation. |
| Capture-to-result latency | **52.9 ms mean / 79.4 ms P95** | 647 inferences on an RTX 3060 Laptop GPU; includes capture, scheduling, execution and readback. |

The roadside car is correctly recognized by both runs; the error is an unnecessary **hazard warning**, not an incorrect object class. During the four seconds after the warning, the image-box baseline slowed to **18.1 km/h**, while fusion stayed at **40.0 km/h**. In a separate in-lane obstacle check, both controllers responded without collision. The vision-only baseline uses manually chosen image thresholds and shares the same route follower; this example does not establish superiority over all camera-only systems.

The comparison uses fixed 30 fps capture and hides other parked-vehicle props identically in both runs to isolate one vehicle. [Baseline rules, setup and capture evidence](docs/VISION_MODEL.md#demo-capture) document the scope. The [fog capture report](Validation/VisionV1/fog-comparison.json) records both runs, visibility settings and the fallback decision. Latency above comes from the separate real-time integration check.

These results describe fixed Unity scenarios. Raycasts are idealized distance sensing with collider bounds, and the controller knows the route. The model does not yet identify drivable space. General road safety, real-camera accuracy and UGen300 performance remain to be evaluated.

[Model, implementation and test details](docs/VISION_MODEL.md) · [Raw integration report](Validation/VisionV1/report.json) · [Numerical parity check](Validation/VisionV1/parity-check.json)

## What UGen300 enables us to test next

| Milestone | Work | Evidence we will collect |
|---|---|---|
| **Deploy the current model** | Calibrate and quantize the two-class detector, compile a Hailo-10H HEF, and connect HailoRT inference to the same Unity controller. | Detection quality before / after quantization and complete scenario runs on the device. |
| **Measure deployment cost** | Compare the GPU and UGen300 using identical inputs and scenarios. | End-to-end mean / P95 latency, throughput, host GPU load, device and whole-system power, and sustained-run stability. |
| **Broaden perception** | Add unseen vehicle assets, roads, lighting and occlusion, followed by real-camera data. Evaluate pedestrian detection and drivable-space segmentation as separate model extensions. | Missed detections, false detections, false braking, clearance and collisions on held-out scenarios. |
| **Build a sensor prototype** | Pair a real camera with physical distance sensing and begin with rider alerts. | Time alignment, distance error, warning timing and usability before considering vehicle actuation. |

ASUS provides a [Windows YOLOv8 / HailoRT demo](https://github.com/Asus-UGen-Series/UGen300-Demo-Package-Windows-User-Guide), and Hailo documents [custom YOLOv8 compilation for Hailo-10H](https://github.com/hailo-ai/hailo_model_zoo/blob/master/training/yolov8/README.rst). These are the deployment starting points; the current ONNX has not been compiled or tested on UGen300. Hardware speed and power improvements will be measured rather than assumed. Generalization work on data and models can continue before the device arrives.

## Run locally

Install **Unity Editor 6000.4.10f1** and Git LFS:

```bash
git clone https://github.com/FlyDragonW/ASUS_AutoDodge.git
cd ASUS_AutoDodge
git lfs pull
```

Open [`Assets/TaiwanRider/Scenes/GongguanRide.unity`](Assets/TaiwanRider/Scenes/GongguanRide.unity) and press **Play**. Select normal riding, car cut-in or crossing scooter. Vision warms up automatically; Raycast control remains available while it is unavailable.

| Key | Action |
|---|---|
| `H` | Toggle vision fusion / Raycast only |
| `F2` | Toggle automatic control |
| `V` | Switch first / third person |
| `G` | Show / hide sensor rays |

The repository contains the Unity source project. See [vision model documentation](docs/VISION_MODEL.md) and the [three-model benchmark](Validation/VisionLargeModels/RESULTS.md) for the automated check, including the parked-car scenario.

Asset attribution: [source manifest](Sources/asset-manifest.json) · [third-party notices](Sources/THIRD_PARTY_NOTICES.txt).
