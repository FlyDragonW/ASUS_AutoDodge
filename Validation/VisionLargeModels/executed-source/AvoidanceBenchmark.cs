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
    public static class AvoidanceBenchmark
    {
        const string Key = "RideReflex.AvoidanceBenchmark";
        static AvoidanceBenchmark() { EditorApplication.playModeStateChanged += OnPlay; }
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
            new GameObject("Avoidance benchmark").AddComponent<AvoidanceBenchmarkRunner>();
        }
    }

    [DefaultExecutionOrder(11000)]
    public sealed class AvoidanceBenchmarkRunner : MonoBehaviour
    {
        [Serializable] public class Spec
        {
            public string id, condition, scenario, weather, controller;
            public int seed;
            public float speedKph, conflictZ, triggerTta, parkedOffset, horizon;
            public float requiredHoldSeconds, blockageReleaseAt, timeLimit;
            public bool fusion;
        }
        [Serializable] public class Config { public string version; public Spec[] cases; }
        [Serializable] public class Sample
        {
            public float seconds, speed, brake, steer, offset, ttc, clearance;
            public int detections, matchedContacts;
            public string state;
        }
        [Serializable] public class Event
        {
            public float seconds, speed, ttc, confidence, ageMs;
            public string source, visualClass, state;
        }
        [Serializable] public class Result
        {
            public Spec spec;
            public string outcome, error;
            public RunResult run;
            public bool valid, collision, completed, safeStop, timedOut;
            public int renderedFrames, staleInferenceFrames, freshMatchedFrames;
            public int interventions, visionAssistedInterventions, extendedRiskSteps, inferenceFrames, matchedFrames, confidentDetectionFrames, fixedSteps;
            public float firstIntervention, minClearance = -1, minSpeed = 999, maxSpeed, maxAbsOffset, maxAbsLateralAcceleration, maxAbsLongitudinalJerk;
            public float meanLatencyMs, p95LatencyMs, actualMeanInferenceHz, elapsedSeconds;
            public float firstStoppedAt=-1, releasedAt=-1, heldStationarySeconds, maxSpeedAfterSettlingBeforeRelease;
            public double wallSeconds;
            public Event[] events;
            public Sample[] telemetry;
        }
        [Serializable] public class Report
        {
            public string version, utc, unity, gpu, error, modelResourcePath;
            public bool completed;
            public int expected, recorded, hiddenParkedProps;
            public float fixedDeltaTime, captureFramerate, timeScale;
            public string timing = "Real time (timeScale=1, captureFramerate=0), 60 FPS target, 50 Hz physics; rolling start at target speed. Not the video capture clock.";
            public Result[] cases;
        }
        ScenarioRunner session;
        RiderVisionSensor vision;
        AutomaticAvoidance pilot;
        Config config;
        string folder;
        Result current;
        readonly List<Result> results = new List<Result>();
        readonly List<Sample> samples = new List<Sample>();
        readonly List<Event> events = new List<Event>();
        readonly List<float> latency = new List<float>();
        readonly List<Collider> obstacles = new List<Collider>();
        GameObject leftBlocker, rightBlocker;
        Vector3 carHome, scooterHome;
        Quaternion carHeading, scooterHeading;
        float carY, originalFogDensity, nextSample, stopSince = -1;
        bool originalFog, exiting;
        Color originalFogColor, originalAmbient;
        AmbientMode originalAmbientMode;
        readonly Dictionary<Camera, float> farClips = new Dictionary<Camera,float>();
        readonly Dictionary<Light,float> lightIntensity = new Dictionary<Light,float>();
        double started;
        int previousCompleted, previousInterventions, hiddenProps;
        float previousForwardSpeed, previousAcceleration;
        bool havePreviousSpeed, havePreviousAcceleration, shot;
        RenderTexture target;
        Texture2D pixels;

        IEnumerator Start()
        {
            string configPath = null;
            var args = Environment.GetCommandLineArgs();
            for (int i=0; i<args.Length-1; i++) {
                if(args[i]=="-benchmarkConfig") configPath=args[i+1];
                if(args[i]=="-benchmarkOutput") folder=args[i+1];
            }
            if(configPath==null || folder==null) { Debug.LogError("Benchmark config/output required"); EditorApplication.Exit(1); yield break; }
            Directory.CreateDirectory(folder);
            config=JsonUtility.FromJson<Config>(File.ReadAllText(configPath));
            session=FindAnyObjectByType<ScenarioRunner>();
            while(!session || !session.Demo) yield return null;
            Application.runInBackground=true;
            Time.captureFramerate=0;Time.timeScale=1;Time.fixedDeltaTime=.02f;
            Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            session.ShowUI=false;session.SuppressResultFiles=true;
            session.rider.cameraMotion=false;session.rider.InputOverride=null;
            session.SetDemoMode(true);session.Demo.SetThirdPerson(true);session.Demo.SetRadarVisible(false);
            foreach(var item in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if(item.name.StartsWith("Parked scooter ") || item.name=="Parked sedan") { item.gameObject.SetActive(false); hiddenProps++; }
            for(int i=0;i<session.scenarios.Length;i++) session.scenarios[i]=Instantiate(session.scenarios[i]);
            carHome=session.car.transform.position;carHeading=session.car.transform.rotation;carY=carHome.y;
            scooterHome=session.scooter.transform.position;scooterHeading=session.scooter.transform.rotation;
            pilot=session.Demo.Pilot;vision=session.Demo.Vision;
            double deadline=Time.realtimeSinceStartupAsDouble+90;
            while(!vision.Ready && vision.Error==null && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            if(!vision.Ready) { Finish("Vision warmup failed: "+vision.Error);yield break; }
            leftBlocker=Instantiate(session.car.gameObject);leftBlocker.name="Benchmark left blocker";
            rightBlocker=Instantiate(session.car.gameObject);rightBlocker.name="Benchmark right blocker";
            foreach(var blocker in new[]{leftBlocker,rightBlocker}) { blocker.GetComponent<HazardActor>().enabled=false;blocker.SetActive(false); }
            CacheWeather();PrepareLabelMaterials();
            target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();
            pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            File.Copy(configPath,Path.Combine(folder,"config.json"),true);
            for(int index=0;index<config.cases.Length;index++)
            {
                var spec=config.cases[index];
                if(vision.Error!=null) {Finish(vision.Error);yield break;}
                Setup(spec);
                current=new Result {spec=spec};
                samples.Clear();events.Clear();latency.Clear();
                previousCompleted=previousInterventions=0;nextSample=0;stopSince=-1;
                havePreviousSpeed=havePreviousAcceleration=shot=false;
                started=Time.realtimeSinceStartupAsDouble;
                Debug.Log("BENCHMARK_START "+index+"/"+config.cases.Length+" "+spec.id);
                while(session.IsRunning && session.elapsed<(spec.timeLimit>0?spec.timeLimit:22) && Time.realtimeSinceStartupAsDouble-started<(spec.timeLimit>22?60:45) && !current.safeStop && vision.Error==null) yield return null;
                current.wallSeconds=Time.realtimeSinceStartupAsDouble-started;
                current.run=session.Result;current.elapsedSeconds=session.elapsed;
                current.collision=current.run.collision;current.completed=current.run.finished;
                current.timedOut=session.IsRunning && !current.safeStop;
                current.outcome=current.collision?"collision":current.completed?"completed":current.safeStop?"safe_stop":"timeout";
                current.interventions=pilot.InterventionCount;
                current.firstIntervention=pilot.FirstInterventionAt;
                current.visionAssistedInterventions=pilot.VisionAssistedInterventions;
                current.extendedRiskSteps=pilot.VisionExtendedRiskSteps;
                current.events=events.ToArray();current.telemetry=samples.ToArray();
                current.inferenceFrames=latency.Count;
                current.actualMeanInferenceHz=latency.Count/Mathf.Max(session.elapsed,.001f);
                if(latency.Count>0) {
                    latency.Sort();float sum=0;foreach(var item in latency)sum+=item;
                    current.meanLatencyMs=sum/latency.Count;current.p95LatencyMs=latency[Mathf.Min(latency.Count-1,Mathf.CeilToInt(latency.Count*.95f)-1)];
                }
                current.valid=vision.Error==null && (!spec.fusion || latency.Count>0);
                current.error=vision.Error;
                if(!shot || current.collision || current.safeStop) Capture(spec.id+"-"+current.outcome);
                results.Add(current);
                File.WriteAllText(Path.Combine(folder,spec.id+".json"),JsonUtility.ToJson(current,true));
                Debug.Log("BENCHMARK_DONE "+index+" "+spec.id+" "+current.outcome+" interventions="+current.interventions+" inference="+current.inferenceFrames);
                current=null;session.ResetToMenu();leftBlocker.SetActive(false);rightBlocker.SetActive(false);
                SaveReport(false,null);
                for(int pause=0;pause<3;pause++)yield return null;
            }
            Finish(null);
        }

        void Setup(Spec s)
        {
            UnityEngine.Random.InitState(s.seed);
            bool car=s.scenario=="cut_in" || s.scenario=="parked" || s.scenario=="roadside" || s.scenario=="blocked_sides";
            int scenario=car?1:s.scenario=="crossing"?2:0;
            var definition=session.scenarios[scenario];definition.seed=s.seed;definition.conflictZ=s.conflictZ;definition.triggerTimeToArrival=s.triggerTta;
            session.car.enabled=true;session.scooter.enabled=true;
            session.car.transform.SetPositionAndRotation(carHome+Vector3.forward*s.conflictZ,carHeading);session.car.Initialize();
            session.scooter.transform.SetPositionAndRotation(scooterHome+Vector3.forward*s.conflictZ,scooterHeading);session.scooter.Initialize();
            session.startPosition=session.route.Position(-36,2.65f,.04f);session.finishZ=22;
            pilot.cruiseSpeedKph=s.speedKph;pilot.ValidationHorizonOverride=s.horizon;
            vision.SetFusionEnabled(s.fusion);
            ApplyWeather(s.weather=="fog");
            session.StartRun(scenario);
            session.rider.Body.linearVelocity=session.rider.transform.forward*(s.speedKph/3.6f);
            obstacles.Clear();leftBlocker.SetActive(false);rightBlocker.SetActive(false);
            if(car)obstacles.Add(session.car.bodyCollider);
            else if(scenario==2)obstacles.Add(session.scooter.bodyCollider);
            if(s.scenario=="parked" || s.scenario=="roadside" || s.scenario=="blocked_sides") {
                session.car.enabled=false;session.car.active=false;
                session.car.transform.SetPositionAndRotation(session.route.Position(s.conflictZ,s.parkedOffset,carY),session.route.Heading(s.conflictZ));
            }
            if(s.scenario=="blocked_sides") {
                PlaceBlocker(leftBlocker,s.conflictZ,.1f);PlaceBlocker(rightBlocker,s.conflictZ,5.2f);
            }
            Physics.SyncTransforms();
        }
        void PlaceBlocker(GameObject go,float z,float offset)
        {
            go.SetActive(true);go.GetComponent<HazardActor>().active=false;
            go.transform.SetPositionAndRotation(session.route.Position(z,offset,carY),session.route.Heading(z));
            obstacles.Add(go.GetComponent<HazardActor>().bodyCollider);
        }
        void FixedUpdate()
        {
            if(current==null || !session.IsRunning)return;
            current.fixedSteps++;
            float speed=session.rider.SpeedKph;
            if(current.spec.blockageReleaseAt>0 && current.releasedAt<0 && session.elapsed>=current.spec.blockageReleaseAt)
            {
                session.car.gameObject.SetActive(false);leftBlocker.SetActive(false);rightBlocker.SetActive(false);
                Physics.SyncTransforms();current.releasedAt=session.elapsed;
            }
            if(current.firstStoppedAt>=0 && current.releasedAt<0 && session.elapsed-current.firstStoppedAt>1)
                current.maxSpeedAfterSettlingBeforeRelease=Mathf.Max(current.maxSpeedAfterSettlingBeforeRelease,speed);
            current.minSpeed=Mathf.Min(current.minSpeed,speed);current.maxSpeed=Mathf.Max(current.maxSpeed,speed);
            float offset=session.rider.transform.position.x-session.route.CenterX(session.rider.transform.position.z);
            current.maxAbsOffset=Mathf.Max(current.maxAbsOffset,Mathf.Abs(offset));
            float forwardSpeed=Vector3.Dot(session.rider.Body.linearVelocity,session.rider.transform.forward);
            float wheelAngle=session.rider.Steer*Mathf.Lerp(session.rider.definition.steeringDegrees,10f,speed/session.rider.definition.topSpeedKph)*Mathf.Deg2Rad;
            current.maxAbsLateralAcceleration=Mathf.Max(current.maxAbsLateralAcceleration,Mathf.Abs(forwardSpeed*forwardSpeed/session.rider.definition.wheelbase*Mathf.Tan(wheelAngle)));
            if(havePreviousSpeed) {
                float acceleration=(forwardSpeed-previousForwardSpeed)/Time.fixedDeltaTime;
                if(havePreviousAcceleration)current.maxAbsLongitudinalJerk=Mathf.Max(current.maxAbsLongitudinalJerk,Mathf.Abs(acceleration-previousAcceleration)/Time.fixedDeltaTime);
                previousAcceleration=acceleration;havePreviousAcceleration=true;
            }
            previousForwardSpeed=forwardSpeed;havePreviousSpeed=true;
            var ego=session.rider.GetComponent<Collider>();
            foreach(var obstacle in obstacles)if(obstacle && obstacle.gameObject.activeInHierarchy) {
                Vector3 near=obstacle.ClosestPoint(ego.bounds.center);
                float clearance=Vector3.Distance(near,ego.ClosestPoint(near));
                if(current.minClearance<0 || clearance<current.minClearance)current.minClearance=clearance;
            }
            if(current.spec.scenario=="blocked_sides" && speed<.5f && pilot.InterventionCount>0 && current.minClearance>.05f) {
                if(stopSince<0)stopSince=session.elapsed;
                if(current.firstStoppedAt<0)current.firstStoppedAt=session.elapsed;
                if(current.releasedAt<0)current.heldStationarySeconds=Mathf.Max(current.heldStationarySeconds,session.elapsed-stopSince);
                if(current.spec.blockageReleaseAt<=0 && session.elapsed-stopSince>=Mathf.Max(2,current.spec.requiredHoldSeconds))current.safeStop=true;
            } else stopSince=-1;
            if(session.elapsed>=nextSample) {
                nextSample=session.elapsed+.1f;
                samples.Add(new Sample {seconds=session.elapsed,speed=speed,brake=session.rider.Brake,steer=session.rider.Steer,offset=offset,
                    ttc=float.IsInfinity(pilot.TimeToCollision)?-1:pilot.TimeToCollision,clearance=current.minClearance,
                    detections=vision.Detections.Count,matchedContacts=vision.MatchedContacts,state=pilot.State.ToString()});
            }
        }
        void LateUpdate()
        {
            if(current==null || exiting)return;
            current.renderedFrames++;
            if(vision.CompletedFrames!=previousCompleted) {
                previousCompleted=vision.CompletedFrames;latency.Add(vision.LastLatencyMs);
                bool stale = Time.realtimeSinceStartupAsDouble - vision.LastCapturedRealtime > RiderVisionSensor.MaxEvidenceAge;
                if(stale)current.staleInferenceFrames++;
                bool freshMatch=false;
                foreach(var contact in session.Demo.Sensor.Contacts)
                    if(vision.TryGetEvidence(contact.collider,out _)){freshMatch=true;break;}
                if(freshMatch)current.freshMatchedFrames++;
                if(vision.MatchedContacts>0)current.matchedFrames++;
                foreach(var d in vision.Detections)if(d.confidence>=RiderVisionSensor.FusionConfidence){current.confidentDetectionFrames++;break;}
            }
            if(pilot.InterventionCount>previousInterventions) {
                previousInterventions=pilot.InterventionCount;
                events.Add(new Event {seconds=session.elapsed,speed=session.rider.SpeedKph,ttc=pilot.LastInterventionTtc,
                    source=pilot.LastDecisionSource,visualClass=pilot.LastVisualClass,confidence=pilot.LastVisualConfidence,
                    ageMs=pilot.LastVisualAgeMs,state=pilot.State.ToString()});
                // One diagnostic per condition/controller; rendering remains part of the measured loop.
                if(!shot) {Capture(current.spec.id+"-intervention");shot=true;}
            }
        }
        void Capture(string name)
        {
            var camera=session.Demo.ActiveCamera;var old=RenderTexture.active;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0,false);pixels.Apply(false);RenderTexture.active=old;
            File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());
        }
        void CacheWeather()
        {
            originalFog=RenderSettings.fog;originalFogDensity=RenderSettings.fogDensity;originalFogColor=RenderSettings.fogColor;
            originalAmbientMode=RenderSettings.ambientMode;originalAmbient=RenderSettings.ambientLight;
            foreach(var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))farClips[camera]=camera.farClipPlane;
            foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.type==LightType.Directional)lightIntensity[light]=light.intensity;
        }
        void ApplyWeather(bool fog)
        {
            RenderSettings.fog=fog || originalFog;RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogDensity=fog?.45f:originalFogDensity;RenderSettings.fogColor=fog?new Color(.44f,.48f,.50f):originalFogColor;
            RenderSettings.ambientMode=fog?AmbientMode.Flat:originalAmbientMode;
            RenderSettings.ambientLight=fog?new Color(.43f,.46f,.48f):originalAmbient;
            foreach(var pair in farClips) {pair.Key.farClipPlane=fog?8:pair.Value;pair.Key.clearFlags=fog?CameraClearFlags.SolidColor:CameraClearFlags.Skybox;pair.Key.backgroundColor=RenderSettings.fogColor;}
            foreach(var pair in lightIntensity)pair.Key.intensity=fog?.65f:pair.Value;
        }
        void PrepareLabelMaterials()
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
        void SaveReport(bool complete,string error)
        {
            var report=new Report {version=config.version,utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,error=error,
                modelResourcePath=vision ? vision.ModelResourcePath : null,
                completed=complete,expected=config.cases.Length,recorded=results.Count,hiddenParkedProps=hiddenProps,
                fixedDeltaTime=Time.fixedDeltaTime,captureFramerate=Time.captureFramerate,timeScale=Time.timeScale,cases=results.ToArray()};
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));
        }
        void Finish(string error)
        {
            if(exiting)return;exiting=true;current=null;
            SaveReport(error==null && results.Count==config.cases.Length,error);
            if(error!=null)Debug.LogError(error);
            EditorApplication.Exit(error==null?0:1);
        }
        void OnDestroy(){if(target){target.Release();Destroy(target);}if(pixels)Destroy(pixels);}
    }
}
