# Gongguan Rider

**A scooter hazard-avoidance simulator for dense urban traffic, with a path from explainable rules to on-device vision AI.**

**[ASUS UGen AI League 2026](https://contest.bhuntr.com/en/39jg9vimiynrhlksze/home/) · target theme: Everyday AI · target battlefield: Lightning (ASUS UGen300)**

> **Submission snapshot:** The playable Unity demo uses 68 physics Raycasts as a simulated range sensor and a rule-based controller. Camera recognition, machine learning, and UGen300 inference are the proposed next stage; they are not implemented in this snapshot.

## Problem and opportunity

A car cutting into a scooter's lane and another scooter crossing from a side street may both appear as nearby obstacles, but they require different responses. Gongguan Rider makes these short, lateral urban conflicts repeatable in a Gongguan-inspired Unity scene. The rider agent must detect the hazard, choose between yielding and steering around it, and return to its lane.

Our first use case is a **scenario-based evaluation kit for scooter rider-assistance research**. Teams can replay a dangerous encounter, inspect why the controller intervened, and compare future AI-assisted decisions with a working baseline before testing on physical vehicles.

## Working demo

The images below are direct Unity camera captures **without in-game UI**. Colored lines visualize the simulated sensor in the 3D scene.

![The scooter steers left around a car cutting into its path](Validation/RadarControls/car-cut-in-no-ui.png)

**Car cut-in:** the controller predicts a conflict, checks side clearance, steers left, and recovers toward the lane. Red rays mark the current threat.

![The scooter brakes for a scooter crossing from a side street](Validation/RadarControls/scooter-crossing-no-ui.png)

**Crossing scooter:** the controller detects lateral movement, brakes to yield, and resumes cruising after the risk clears. Cyan rays have no hit; orange rays hit obstacles; the ground curve marks the target path.

### Recorded results

| Scripted Unity run | Observed controller response | Result |
| --- | --- | --- |
| Car cut-in at 40 and 50 km/h | Evade left → recover → cruise | Both runs reached the finish without a recorded collision. |
| Side-street scooter at 40 and 50 km/h | Brake → cruise | Both runs reached the finish without a recorded collision. |
| Clear road | Continue cruising | No unnecessary intervention recorded. |

The [validation report](Validation/RadarControls/acceptance.json) records **32/32 passing checks and 0 errors**. It also covers view changes, hiding the sensor visualization while detection stays active, pause/retry, and manual takeover. These are scripted simulation results, **not** evidence of road safety or performance on unseen traffic.

## Implemented system

| Layer | Current implementation |
| --- | --- |
| World | [Gongguan ride scene](Assets/TaiwanRider/Scenes/GongguanRide.unity), built with Unity 6 (6000.4.10f1) and URP 17.4. Road alignment uses [OpenStreetMap centerline data](Sources/gongguan-osm.json); road width and streetscape details are reconstructed estimates. |
| Sensing | [RadarSensor.cs](Assets/TaiwanRider/Runtime/RadarSensor.cs) runs 68 `Physics.Raycast` queries per physics update: 44 around the scooter and 24 within ±40° ahead. Maximum range is 32 m forward, 16 m sideways, and 10 m behind. |
| Prediction | Consecutive collider observations estimate relative motion; [AutomaticAvoidance.cs](Assets/TaiwanRider/Runtime/AutomaticAvoidance.cs) checks a roughly 1.8 s collision horizon. |
| Action | A transparent state machine chooses cruise, brake, evade left/right, recover, or emergency stop and drives throttle, braking, and steering through the scooter controller. |

```text
Unity traffic → simulated range rays → contact tracking → collision-risk estimate
              → rule-based safety policy → throttle / brake / steering → scooter
```

The current “radar” is a geometric game-engine approximation. It does not simulate radio propagation, noise, weather interference, or a physical radar unit.

## Edge AI plan for Battlefield Lightning

The planned extension uses camera images to identify road users and drivable space, then combines those cues with temporal motion estimates before the safety policy acts. The host handles tracking and control; the [ASUS UGen300](https://press.asus.com/news/press-releases/asus-ugen300-usb-ai-accelerator-generative-ai-edge/) is the proposed local inference accelerator. No camera footage needs to leave the device during inference.

```text
Unity RGB frames → compact vision model on UGen300 → road-user / road-space cues
                 → host-side tracking and risk fusion → explicit safety policy
                 → simulated scooter controls
```

The existing controller is the **rules-only baseline and fallback**. This makes the AI contribution testable: a vision-assisted controller must handle more varied hazards without increasing unsafe or unnecessary interventions.

| Stage II milestone | Deliverable | Evaluation |
| --- | --- | --- |
| Build data | Vary road layout, actor timing, lighting, and camera viewpoint; derive labels from the simulator; reserve unseen combinations for testing. | Published scenario split and failure cases, separated by scenario rather than adjacent video frames. |
| Run local perception | Train a compact road-user and drivable-space model and deploy compatible inference on UGen300. | Recognition quality, frame rate, end-to-end latency, and device power measured on the actual hardware. |
| Close the loop | Fuse vision with motion history and the explicit safety policy; compare against the current controller on the same held-out scenarios. | Collision and near-miss rate, minimum clearance, false braking, and intervention time. |

The first product hypothesis is a repeatable scenario and benchmark package for scooter makers and rider-assistance teams. A later on-vehicle application would need physical sensors, real-world data, independent safety validation, and a separate deployment design; this simulator is not a road-ready assistance system.

## Reproduce the demo

Install **Unity Editor 6000.4.10f1** and Git LFS. Fetch the full repository, including large art assets:

```bash
git clone https://github.com/FlyDragonW/ASUS_AutoDodge.git
cd ASUS_AutoDodge
git lfs pull
```

Open the repository folder in Unity, then open [`Assets/TaiwanRider/Scenes/GongguanRide.unity`](Assets/TaiwanRider/Scenes/GongguanRide.unity) and press **Play**. Choose normal riding, car cut-in, or crossing scooter from the menu; automatic mode starts at 40 km/h. The repository contains the Unity source project, not a packaged Windows player.

| Control | Key |
| --- | --- |
| Toggle automatic control / manual takeover | F2 |
| Switch first / third person | V |
| Show / hide Raycast visualization | G |
| Manual throttle / brake / steer | W / S / A / D |
| Retry / pause | R / Esc |

The [Unity editor tests](Assets/TaiwanRider/Tests/Editor/RiderSceneTests.cs) check scene and component references. Asset origins and reuse terms are documented in the [source manifest](Sources/asset-manifest.json) and [third-party notices](Sources/THIRD_PARTY_NOTICES.txt).

---

**繁體中文摘要：**公館機車自動避閃是可重現的 Unity 都市交通模擬原型。目前以 Raycast 模擬距離感測，利用相對運動預測風險，透過規則式控制煞車、繞行與回正；四組腳本化危險情境皆抵達終點，驗證紀錄為 32/32 通過。下一階段規劃以 ASUS UGen300 執行本地相機辨識，並在未見過的情境中與現有基準比較碰撞、誤煞、反應時間、延遲和功耗。**相機 AI 與 UGen300 部署目前尚未完成。**
