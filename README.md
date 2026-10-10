# Gongguan Rider

**A simulation-first scooter hazard-avoidance prototype, built in Taipei and designed to grow into local, camera-based AI.**

Proposed for the [ASUS UGen AI League](https://contest.bhuntr.com/en/39jg9vimiynrhlksze/home/) under **Everyday AI / Battlefield Lightning (UGen300)**. The current playable prototype uses simulated radar and deterministic rules; camera perception, machine learning, and UGen300 deployment are **planned work**, not features of the present build.

## The idea in 30 seconds

Scooters meet sudden car cut-ins and vehicles crossing from side streets in dense urban traffic. Gongguan Rider turns those encounters into repeatable Unity scenarios where a rider agent must sense the hazard, decide whether to brake or steer, and return to its lane. The first version establishes a measurable safety baseline. The next version will add on-device visual perception so the same decision loop can be tested against less scripted situations without sending camera footage to the cloud.

The immediate use case is a **scenario and evaluation platform** for rider-assistance research and training. A road-going assistance system would require real sensors, much broader validation, and safety engineering beyond this prototype.

## See the current prototype

These are direct Unity camera captures **without in-game UI**. Colored lines are the simulated Raycast sensor visualized in the 3D scene.

![The scooter steers left around a car cutting into its path](Validation/RadarControls/car-cut-in-no-ui.png)

**Car cut-in:** forward rays detect the car; the controller checks the predicted collision and available side space, steers left, then recovers toward the original lane. Red rays mark the current threat.

![The scooter brakes for another scooter crossing from a side street](Validation/RadarControls/scooter-crossing-no-ui.png)

**Crossing scooter:** the controller detects lateral motion, brakes to yield, and resumes cruising when the risk clears. Cyan rays have no hit, orange rays hit obstacles, and the ground curve shows the target path.

## What works today

| Component | Implemented behavior |
| --- | --- |
| Simulator | [Gongguan road scene](Assets/TaiwanRider/Scenes/GongguanRide.unity) in Unity 6 (6000.4.10f1) with URP 17.4; car cut-in and side-street scooter scenarios. |
| Sensing | [RadarSensor.cs](Assets/TaiwanRider/Runtime/RadarSensor.cs) casts 68 `Physics.Raycast` rays per physics update: 44 around the rider and 24 concentrated within ±40° ahead. Range varies by direction: up to 32 m ahead, 16 m to the side, and 10 m behind. |
| Risk estimate | Tracks collider positions across scans, estimates relative motion, and evaluates a roughly 1.8 s collision horizon. |
| Decision and control | [AutomaticAvoidance.cs](Assets/TaiwanRider/Runtime/AutomaticAvoidance.cs) chooses among cruise, brake, evade left/right, recover, and emergency stop, then sends throttle, brake, and steering commands to the scooter controller. |

The [recorded validation run](Validation/RadarControls/acceptance.json) has **32/32 checks passing and 0 recorded errors**. In the four scripted hazard runs (car cut-in and crossing scooter at 40 and 50 km/h), the scooter intervened and reached the finish without a recorded collision. The report also checks clear-road cruising, sensor display toggling, camera views, retry, pause, and manual takeover. These results describe those scripted Unity runs only; they are not a measured real-world crash reduction or evidence of generalization.

The “radar” here is geometric Raycast sensing in a game engine. It does **not** model radio propagation, sensor noise, weather interference, or a physical radar unit.

## Why this is a useful AI starting point

- **Scooter-specific hazards:** car cut-ins and side-street crossings demand different actions, even when both first appear as an obstacle ahead.
- **A closed-loop baseline:** sensing, prediction, maneuver selection, and vehicle response already run together. A future vision model must improve decisions in the same scenarios, not merely recognize objects in isolated images.
- **Testable failures:** the scripted runs and saved report make it possible to replay a hazardous encounter, change the scene, and compare a vision-assisted controller against the rules-only version.

## Technical architecture

```text
Implemented today
Unity traffic actors → 68 Raycasts → contact/relative-motion tracking
                     → collision-risk estimate → rule-based safety policy
                     → throttle / brake / steering → Unity scooter

Planned AI extension
RGB camera → compact vision model on ASUS UGen300 → road-user / drivable-space cues
           → temporal tracking + risk fusion → safety policy → rider action
```

The rule-based controller gives us an interpretable baseline and a safety fallback while we develop perception. The planned vision model will supply cues that fixed geometric rules miss in more varied scenes; its outputs will be compared with the baseline on held-out scenarios. ASUS positions the [UGen300](https://press.asus.com/news/press-releases/asus-ugen300-usb-ai-accelerator-generative-ai-edge/) as a local AI accelerator for vision and other inference workloads. Hardware integration and its actual throughput, latency, and power use remain to be measured.

## Development and evaluation plan

| Step | Deliverable | Evidence we will collect |
| --- | --- | --- |
| 1. Expand scenarios | Vary traffic spawn timing, road layout, lighting, weather, and camera viewpoint; keep unseen combinations for evaluation. | Scenario manifest and train/test split with no reused scripted sequence in the held-out set. |
| 2. Add vision | Label camera frames for vehicles and drivable space; train a compact model and run local inference on UGen300. | Perception quality, frame rate, end-to-end latency, and device power on the target hardware. |
| 3. Close the loop | Fuse visual cues with motion history; retain an explicit braking/steering safety policy and compare it with the current rules-only baseline. | Collision and near-miss rate, minimum clearance, false braking, and intervention time across unseen scenarios. |

The first adoption hypothesis is a scenario-based evaluation kit for scooter makers and rider-assistance research teams: repeatable urban hazards, baseline controller, and comparable safety metrics. A training mode for riding schools is a possible later extension. We have not validated customer demand or a business model yet. Any on-vehicle version must replace simulated range data with physical sensors and pass independent safety testing.

## Run the project

1. Install **Unity Editor 6000.4.10f1**. Install Git LFS, then clone this repository and run `git lfs pull` to fetch the textures, models, and fonts.
2. Open the repository folder as a Unity project. Open [`Assets/TaiwanRider/Scenes/GongguanRide.unity`](Assets/TaiwanRider/Scenes/GongguanRide.unity) and press **Play**.
3. Choose normal riding, car cut-in, or crossing scooter in the main menu. The automatic demo starts at 40 km/h.

The project includes the Unity scene, scripts, art assets, `.meta` files, package manifest, and build settings. The [asset notices](Sources/THIRD_PARTY_NOTICES.txt) and [source manifest](Sources/asset-manifest.json) document external material; the [editor tests](Assets/TaiwanRider/Tests/Editor/RiderSceneTests.cs) check key scene and component references. A packaged Windows player is not part of this repository.

## Demo controls

These controls work in the Unity Play view:

| Action | Key |
| --- | --- |
| Toggle automatic control / manual takeover | F2 |
| Switch first / third person | V |
| Show / hide Raycast visualization | G |
| Manual throttle / brake / steer | W / S / A / D |
| Retry / pause | R / Esc |

---

**繁體中文摘要：**本專案以台北公館機車情境驗證自動避閃。現階段使用 Unity Raycast 模擬雷達，依相對運動與約 1.8 秒碰撞風險，透過規則式控制煞車、繞行與回正。已保存汽車切入與機車橫越的無 UI 實機截圖和測試紀錄。下一階段規劃在 ASUS UGen300 上加入相機辨識及機器學習，並以未見過的場景量測避閃成效、誤煞、延遲與功耗；目前尚未完成 AI 或硬體部署。
