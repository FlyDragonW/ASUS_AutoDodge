using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace TaiwanRider
{
    /// <summary>Opt-in acceptance fixture. Never drives a normal player session.</summary>
    public sealed class RiderAcceptance : MonoBehaviour
    {
        public ScenarioRunner session;
        public RoadRoute route;
        public string Status="idle";
        public string LastReport;
        public bool Busy {get;private set;}
        readonly List<float> frameTimes=new List<float>();
        readonly List<string> errors=new List<string>();
        readonly List<Check> checks=new List<Check>();
        float benchmarkStart=-1;
        [Serializable] public class Check {public string name;public bool passed;public string detail;}
        [Serializable] public class Report
        {
            public string capturedAtUtc,device,output,applicationVersion;
            public int width,height,frames;
            public float seconds,averageFps,onePercentLowFps;
            public Check[] checks;
            public string[] errors;
        }
        void Start()
        {
            Application.logMessageReceived+=Log;
            string[] args=Environment.GetCommandLineArgs();
            if(args.Contains("-rider-benchmark"))
            {
                Application.runInBackground=true;
                int index=Array.IndexOf(args,"-duration");float duration=index>=0?float.Parse(args[index+1],System.Globalization.CultureInfo.InvariantCulture):600;
                StartCoroutine(Benchmark(duration,true));
            }
            else if(args.Contains("-rider-smoke")){Application.runInBackground=true;StartCoroutine(Smoke(true));}
        }
        void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message);}
        void Update(){if(benchmarkStart>=0 && Time.unscaledTime-benchmarkStart>30 && Time.unscaledDeltaTime>0)frameTimes.Add(Time.unscaledDeltaTime);}
        public void StartSmoke(){if(!Busy)StartCoroutine(Smoke(false));}
        public void StartGameplayChecks(){if(!Busy)StartCoroutine(GameplayChecks());}
        public void StartBrakeCheck(){if(!Busy)StartCoroutine(BrakeCheck());}
        public void StartBenchmark(float duration=600){if(!Busy)StartCoroutine(Benchmark(duration,false));}
        void CheckResult(string name,bool passed,string detail)
        {checks.Add(new Check{name=name,passed=passed,detail=detail});Debug.Log("ACCEPTANCE "+name+": "+(passed?"PASS ":"FAIL ")+detail);}
        string OutputDirectory()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-rider-output");
            string folder=i>=0?args[i+1]:Path.Combine(Application.persistentDataPath,"Validation");Directory.CreateDirectory(folder);return folder;
        }
        void Follow(TestScooterInput input,float throttle=.8f)
        {
            var rider=session.rider;float z=rider.transform.position.z;
            float lookAhead=6f+rider.SpeedKph/3.6f*.35f;
            var target=route.Position(z+lookAhead,2.65f,rider.transform.position.y);
            float angle=Vector3.SignedAngle(rider.transform.forward,target-rider.transform.position,Vector3.up);
            float wheelAngle=Mathf.Atan2(2*rider.definition.wheelbase*Mathf.Sin(angle*Mathf.Deg2Rad),lookAhead)*Mathf.Rad2Deg;
            float available=Mathf.Lerp(rider.definition.steeringDegrees,10f,rider.SpeedKph/rider.definition.topSpeedKph);
            input.steer=Mathf.Clamp(wheelAngle/available,-.8f,.8f);input.throttle=throttle;input.brake=0;
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            if(!Environment.GetCommandLineArgs().Contains("-rider-capture-camera"))
            {ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory(),name+".png"));yield break;}
            // A hidden Windows test window may have no presented backbuffer. Render explicitly
            // so the packaged shaders, mirrors and scene can still be checked on the real GPU.
            Camera camera=session.Demo?session.Demo.ActiveCamera:session.rider.riderCamera;
            var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                var request=new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target};
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
                var pixels=texture.GetPixels32();int low=255,high=0;
                for(int i=0;i<pixels.Length;i+=127){int brightness=(pixels[i].r+pixels[i].g+pixels[i].b)/3;low=Math.Min(low,brightness);high=Math.Max(high,brightness);}
                CheckResult(name+" camera renders",high-low>10,"brightness range="+(high-low));
                File.WriteAllBytes(Path.Combine(OutputDirectory(),name+".png"),texture.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;target.Release();Destroy(target);Destroy(texture);}
        }
        IEnumerator GameplayChecks()
        {
            Busy=true;checks.Clear();errors.Clear();session.SuppressResultFiles=true;
            var rider=session.rider;var input=new TestScooterInput();rider.InputOverride=input;
            Transform cameraParent=rider.cameraRig.parent;
            session.StartRun(0);Status="100 km/h limit";
            rider.ResetVehicle(route.Position(-40,2.65f,.025f),route.Heading(-40));
            rider.Body.linearVelocity=rider.transform.forward*(110f/3.6f);Physics.SyncTransforms();
            for(int i=0;i<8;i++){Follow(input,1);yield return new WaitForFixedUpdate();}
            CheckResult("100 km/h cap enforced",rider.SpeedKph>98 && rider.SpeedKph<100.5f,"speed="+rider.SpeedKph);
            yield return Capture("gameplay-100-kph");
            foreach(float approachSpeed in new[]{40f,100f})for(int mode=1;mode<=2;mode++)
            {
                session.StartRun(mode);Status="hazard "+mode+" at "+approachSpeed;
                var actor=mode==1?session.car:session.scooter;
                rider.ResetVehicle(route.Position(-45,2.65f,.025f),route.Heading(-45));
                rider.Body.linearVelocity=rider.transform.forward*(approachSpeed/3.6f);Physics.SyncTransforms();
                CheckResult(Status+" model faces forward",Vector3.Dot(actor.ModelForward,actor.transform.forward)>.98f,"dot="+Vector3.Dot(actor.ModelForward,actor.transform.forward));
                float limit=Time.time+12,triggerTtc=-1,minForwardDot=1;Vector3 previousActorPosition=actor.transform.position;
                while(session.IsRunning && Time.time<limit)
                {
                    Follow(input,.5f);
                    if(session.EventStartedAt>=0 && triggerTtc<0)
                        triggerTtc=(session.scenarios[mode].conflictZ-rider.transform.position.z)/Mathf.Max(.01f,rider.SpeedKph/3.6f);
                    Vector3 delta=actor.transform.position-previousActorPosition;
                    if(delta.sqrMagnitude>.00001f)minForwardDot=Mathf.Min(minForwardDot,Vector3.Dot(actor.ModelForward,delta.normalized));
                    previousActorPosition=actor.transform.position;
                    yield return new WaitForFixedUpdate();
                }
                CheckResult(Status+" short warning",triggerTtc>0 && triggerTtc<=session.scenarios[mode].triggerTimeToArrival+.06f,"TTC="+triggerTtc);
                CheckResult(Status+" drives nose first",minForwardDot>.45f,"min dot="+minForwardDot);
                CheckResult(Status+" impact",session.Result.collision && session.Result.hitObject==actor.gameObject.name,"hit="+session.Result.hitObject);
                bool launched=rider.HasCrashed && rider.Crash.RiderBody && rider.cameraRig.parent!=cameraParent;
                CheckResult(Status+" ejects rider",launched && rider.Body.constraints==RigidbodyConstraints.None,"detached="+launched);
                if(launched)
                {
                    var crashBody=rider.Crash.RiderBody;Vector3 origin=crashBody.position;Quaternion rotation=crashBody.rotation;
                    yield return new WaitForSeconds(.3f);
                    CheckResult(Status+" airborne tumble",crashBody.position.y>origin.y+.15f && Vector3.Distance(crashBody.position,origin)>.5f && Quaternion.Angle(rotation,crashBody.rotation)>15,"rise="+(crashBody.position.y-origin.y)+", travel="+Vector3.Distance(crashBody.position,origin));
                    if(approachSpeed==40)yield return Capture("gameplay-crash-"+mode);
                }
                input.throttle=0;input.brake=1;input.steer=0;
                session.StartRun(mode);yield return new WaitForSeconds(.3f);
                CheckResult(Status+" retry restores rider",!rider.HasCrashed && rider.cameraRig.parent==cameraParent && rider.SpeedKph<.5f && !session.Result.hazardTriggered && Vector3.Distance(rider.transform.position,session.startPosition)<.1f,"speed="+rider.SpeedKph);
            }
            session.StartRun(0);input.throttle=0;input.brake=1;input.steer=0;
            session.TogglePause();Vector3 paused=rider.transform.position;yield return new WaitForSecondsRealtime(.2f);
            CheckResult("Pause remains stable",session.IsPaused && Vector3.Distance(paused,rider.transform.position)<.001f,"paused="+session.IsPaused);session.TogglePause();
            WriteReport("gameplay-update",0);Status=checks.All(c=>c.passed)&&errors.Count==0?"passed":"failed";
            rider.InputOverride=null;session.SuppressResultFiles=false;Busy=false;session.ResetToMenu();
        }
        IEnumerator Smoke(bool quit)
        {
            Busy=true;checks.Clear();errors.Clear();session.SuppressResultFiles=true;
            var input=new TestScooterInput();session.rider.InputOverride=input;
            Status="menu capture";yield return new WaitForSeconds(2);yield return Capture("01-menu");
            session.StartRun(0);Status="acceleration";
            float end=Time.time+7;
            while(Time.time<end && session.IsRunning){Follow(input,1);yield return null;}
            CheckResult("Throttle accelerates",session.rider.SpeedKph>12,"speed="+session.rider.SpeedKph.ToString("F2"));
            yield return Capture("02-riding");
            input.throttle=0;input.brake=1;end=Time.time+4;
            while(Time.time<end)yield return null;
            CheckResult("Brakes stop",session.rider.SpeedKph<1,"speed="+session.rider.SpeedKph.ToString("F2"));
            Vector3 stopped=session.rider.transform.position;yield return new WaitForSeconds(2);
            CheckResult("Stopped scooter stays put",Vector3.Distance(stopped,session.rider.transform.position)<.15f,"drift="+Vector3.Distance(stopped,session.rider.transform.position).ToString("F3"));
            session.StartRun(0);Quaternion beforeTurn=session.rider.Body.rotation;input.throttle=.5f;input.brake=0;input.steer=.4f;
            yield return new WaitForSeconds(1.5f);
            CheckResult("Steering changes heading",Quaternion.Angle(beforeTurn,session.rider.Body.rotation)>2,"yaw change="+Quaternion.Angle(beforeTurn,session.rider.Body.rotation));
            session.StartRun(0);input.throttle=0;input.brake=1;
            CheckResult("Retry resets run",!session.Result.hazardTriggered && session.elapsed<.1f && session.rider.SpeedKph<.1f,"event="+session.EventStartedAt);
            session.TogglePause();stopped=session.rider.transform.position;yield return new WaitForSecondsRealtime(.5f);
            CheckResult("Pause freezes state",Vector3.Distance(stopped,session.rider.transform.position)<.001f && session.elapsed<.1f,"paused="+session.IsPaused);session.TogglePause();
            for(int mode=1;mode<=2;mode++)
            {
                session.StartRun(mode);Status="hazard "+mode;float limit=Time.time+45;bool captured=false;
                while(session.IsRunning && Time.time<limit)
                {
                    Follow(input,.78f);
                    if(session.Result.hazardTriggered && !captured){captured=true;yield return Capture("0"+(2+mode)+"-hazard");}
                    yield return null;
                }
                CheckResult("Hazard "+mode+" triggers",session.Result.hazardTriggered,"collision="+session.Result.collision+", elapsed="+session.elapsed);
                CheckResult("Hazard "+mode+" no-action collision",session.Result.collision,"hit="+session.Result.hitObject+", clearance="+session.Result.minimumClearanceMetres+", eventTime="+session.EventStartedAt);
                session.StartRun(mode);CheckResult("Hazard "+mode+" reset",!session.Result.hazardTriggered && !session.car.active && !session.scooter.active,"fresh run");
            }
            yield return BrakeAvoidance(input);
            yield return Capture("05-result");
            WriteReport("smoke",0);Status=checks.All(c=>c.passed)&&errors.Count==0?"passed":"failed";
            session.rider.InputOverride=null;session.SuppressResultFiles=false;Busy=false;
            if(quit)Application.Quit(Status=="passed"?0:1);else session.ResetToMenu();
        }
        IEnumerator BrakeAvoidance(TestScooterInput input)
        {
            session.StartRun(2);Status="brake avoidance";bool reacted=false;float resumeAt=0,timeout=Time.time+65;
            float triggerSpeed=0,triggerZ=0;
            while(session.IsRunning && Time.time<timeout)
            {
                Follow(input,.78f);
                if(session.Result.hazardTriggered && !reacted){reacted=true;resumeAt=Time.time+5;triggerSpeed=session.rider.SpeedKph;triggerZ=session.rider.transform.position.z;}
                if(reacted && Time.time<resumeAt){input.throttle=0;input.brake=1;}
                yield return null;
            }
            CheckResult("Braking avoids crossing",reacted && session.Result.finished && !session.Result.collision,"reaction="+session.Result.reactionSeconds+", clearance="+session.Result.minimumClearanceMetres+", hit="+session.Result.hitObject+", triggerSpeed="+triggerSpeed+", triggerZ="+triggerZ+", endZ="+session.rider.transform.position.z+", elapsed="+session.elapsed);
        }
        IEnumerator BrakeCheck()
        {
            Busy=true;checks.Clear();errors.Clear();session.SuppressResultFiles=true;
            var input=new TestScooterInput();session.rider.InputOverride=input;
            yield return BrakeAvoidance(input);
            WriteReport("brake-avoidance",0);Status=checks.All(c=>c.passed)&&errors.Count==0?"passed":"failed";
            session.rider.InputOverride=null;session.SuppressResultFiles=false;Busy=false;session.ResetToMenu();
        }
        IEnumerator Benchmark(float duration,bool quit)
        {
            Busy=true;checks.Clear();errors.Clear();frameTimes.Clear();
            session.SuppressResultFiles=true;var input=new TestScooterInput();session.rider.InputOverride=input;
            yield return new WaitForSeconds(3);session.StartRun(0);benchmarkStart=Time.unscaledTime;
            int run=0;bool capture=false;
            while(Time.unscaledTime-benchmarkStart<duration)
            {
                Status="benchmark "+(Time.unscaledTime-benchmarkStart).ToString("F0")+" / "+duration;
                if(!session.IsRunning){run++;session.StartRun(run%3);}
                Follow(input,.85f);
                if(!capture && Time.unscaledTime-benchmarkStart>45){capture=true;yield return Capture("benchmark-1080p");}
                yield return null;
            }
            CheckResult("Frame loop advances",frameTimes.Count>1000,"frames="+frameTimes.Count);
            CheckResult("No runtime errors",errors.Count==0,"errors="+errors.Count);
            WriteReport("benchmark",Time.unscaledTime-benchmarkStart);benchmarkStart=-1;Status="benchmark complete";
            session.rider.InputOverride=null;session.SuppressResultFiles=false;Busy=false;
            if(quit)Application.Quit(errors.Count==0?0:1);else session.ResetToMenu();
        }
        void WriteReport(string name,float seconds)
        {
            float average=frameTimes.Count>0?1f/frameTimes.Average():0;
            float low=frameTimes.Count>0?1f/frameTimes.OrderByDescending(x=>x).Take(Mathf.Max(1,frameTimes.Count/100)).Average():0;
            var report=new Report{capturedAtUtc=DateTime.UtcNow.ToString("O"),applicationVersion=Application.version,device=SystemInfo.graphicsDeviceName,width=Screen.width,height=Screen.height,frames=frameTimes.Count,seconds=seconds,averageFps=average,onePercentLowFps=low,checks=checks.ToArray(),errors=errors.ToArray(),output=OutputDirectory()};
            LastReport=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(OutputDirectory(),name+".json"),LastReport);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
