using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TaiwanRider
{
    public sealed class DemoAcceptance : MonoBehaviour
    {
        public RiderDemo demo;
        public string Status="idle",LastReport;
        public bool Busy {get;private set;}
        [Serializable] public class Check {public string name;public bool passed;public string detail;}
        [Serializable] public class Report {public string capturedAtUtc;public Check[] checks;public string[] errors;}
        readonly List<Check> checks=new List<Check>();
        readonly List<string> errors=new List<string>();
        void Awake(){Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(Busy && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert))errors.Add(message);}
        void CheckResult(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});Debug.Log("DEMO CHECK "+name+" "+(passed?"PASS ":"FAIL ")+detail);}
        public void StartChecks(bool extended=false){if(!Busy)StartCoroutine(Run(extended));}
        string Folder {get {string path=Path.Combine(Application.persistentDataPath,"Validation","RadarControls");Directory.CreateDirectory(path);return path;}}
        IEnumerator Capture(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));}
        IEnumerator Run(bool extended)
        {
            Busy=true;checks.Clear();errors.Clear();var session=demo.session;
            session.SuppressResultFiles=true;session.rider.InputOverride=null;session.SetDemoMode(true);
            CheckResult("Ray budget reduced by 40 percent",demo.Sensor.Beams.Length==68,"casts="+demo.Sensor.Beams.Length+", previous=113");
            foreach(float speed in extended?new[]{40f,50f}:new[]{40f})for(int mode=1;mode<=2;mode++)
            {
                bool thirdPerson=(speed==40)==(mode==1),visible=speed==40;
                demo.SetThirdPerson(thirdPerson);demo.SetRadarVisible(visible);
                demo.Pilot.cruiseSpeedKph=speed;session.StartRun(mode);Status="scenario "+mode+" / "+speed+" km/h / "+(thirdPerson?"third":"first")+" person / rays "+visible;
                float limit=Time.time+60,maxLateral=0;bool captured=false;string states="";AvoidanceState previous=(AvoidanceState)(-1);
                while(session.IsRunning && Time.time<limit)
                {
                    float offset=session.rider.transform.position.x-session.route.CenterX(session.rider.transform.position.z);
                    maxLateral=Mathf.Max(maxLateral,Mathf.Abs(offset-2.65f));
                    if(previous!=demo.Pilot.State){previous=demo.Pilot.State;states+=session.elapsed.ToString("F2")+":"+previous+"@"+(demo.Pilot.Threat?demo.Pilot.Threat.name:"clear")+" ";}
                    if(!captured && demo.Pilot.FirstInterventionAt>=0 && session.elapsed-demo.Pilot.FirstInterventionAt>.3f)
                    {captured=true;yield return Capture("demo-"+mode+"-"+speed);}
                    yield return null;
                }
                CheckResult(Status+" reaches finish",session.Result.finished && !session.Result.collision,"hit="+session.Result.hitObject+", z="+session.rider.transform.position.z+", states="+states);
                CheckResult(Status+" senses and intervenes",session.Result.hazardTriggered && demo.Pilot.InterventionCount>0 && demo.Pilot.FirstInterventionAt>=session.EventStartedAt-.1f,"event="+session.EventStartedAt+", action="+demo.Pilot.FirstInterventionAt);
                CheckResult(Status+" correct radar visibility",demo.Display.RenderedBeamCount==(visible?RadarSensor.BeamCount:0),"rendered beams="+demo.Display.RenderedBeamCount+", scans="+demo.Sensor.ScanCount);
                CheckResult(Status+" preserves view and auto control",demo.ThirdPerson==thirdPerson && demo.ActiveCamera.enabled && session.rider.riderCamera.enabled!=thirdPerson && demo.ViewCamera.enabled==thirdPerson && demo.Pilot.IsControlling,"first person="+session.rider.riderCamera.enabled+", auto="+demo.Pilot.IsControlling);
                if(mode==1)
                {
                    CheckResult(Status+" steers around car",maxLateral>.7f,"lateral movement="+maxLateral);
                    CheckResult(Status+" stable recovery",demo.Pilot.InterventionCount==1,"interventions="+demo.Pilot.InterventionCount);
                }
            }
            if(extended)
            {
                demo.Pilot.cruiseSpeedKph=40;session.StartRun(0);Status="clear road cruise";float limit=Time.time+60;
                while(session.IsRunning && Time.time<limit)yield return null;
                CheckResult("Clear road cruise",session.Result.finished && demo.Pilot.InterventionCount==0,"interventions="+demo.Pilot.InterventionCount+", hit="+session.Result.hitObject);
            }
            session.ResetToMenu();demo.SetThirdPerson(false);demo.SetRadarVisible(true);session.StartRun(1);
            yield return new WaitForSeconds(.25f);yield return new WaitForEndOfFrame();
            CheckResult("First person visible radar",session.rider.riderCamera.enabled && demo.Display.RenderedBeamCount==68,"beams="+demo.Display.RenderedBeamCount);
            demo.SetRadarVisible(false);int scans=demo.Sensor.ScanCount;
            yield return new WaitForSeconds(.25f);yield return new WaitForEndOfFrame();
            CheckResult("Hiding rays keeps sensing and steering",demo.Display.RenderedBeamCount==0 && demo.Sensor.ScanCount>scans && demo.Pilot.IsControlling && session.rider.Throttle>0,"scan delta="+(demo.Sensor.ScanCount-scans)+", throttle="+session.rider.Throttle);
            demo.SetThirdPerson(true);yield return null;
            CheckResult("Changing view preserves hidden rays and auto",!demo.Display.Visible && demo.Pilot.IsControlling && demo.ViewCamera.enabled,"third person auto with hidden rays");
            demo.SetThirdPerson(false);session.TogglePause();Vector3 start=session.rider.transform.position;scans=demo.Sensor.ScanCount;
            demo.SetRadarVisible(true);demo.SetRadarVisible(false);
            yield return new WaitForSecondsRealtime(.3f);
            CheckResult("Settings during pause keep simulation frozen",Vector3.Distance(start,session.rider.transform.position)<.001f && scans==demo.Sensor.ScanCount && Cursor.visible,"paused="+session.IsPaused);session.TogglePause();
            session.StartRun(1);
            CheckResult("Retry keeps first person and hidden rays",!demo.ThirdPerson && !demo.Display.Visible && demo.Pilot.IsControlling && session.rider.riderCamera.enabled,"view/radar/auto preserved");
            CheckResult("Demo retry reset",demo.Pilot.InterventionCount==0 && demo.Pilot.State==AvoidanceState.Cruise && !session.rider.HasCrashed && demo.Sensor.ScanCount==0,"state="+demo.Pilot.State);
            session.SetDemoMode(false);CheckResult("Manual takeover keeps first person",!demo.Pilot.IsControlling && session.rider.riderCamera.enabled && !demo.ThirdPerson,"manual first person");
            session.SetDemoMode(true);CheckResult("Enabling auto keeps first person and hidden rays",demo.Pilot.IsControlling && session.rider.riderCamera.enabled && !demo.Display.Visible,"first person auto");
            demo.SetThirdPerson(true);session.SetDemoMode(false);CheckResult("Manual takeover keeps third person",!demo.Pilot.IsControlling && demo.ViewCamera.enabled && !demo.Display.Visible,"manual third person");
            session.SetDemoMode(true);CheckResult("Enabling auto keeps third person",demo.Pilot.IsControlling && demo.ViewCamera.enabled,"third person auto");
            demo.SetThirdPerson(false);demo.SetRadarVisible(true);
            demo.Pilot.cruiseSpeedKph=40;session.selectedScenario=1;session.ResetToMenu();yield return Capture("demo-menu");
            LastReport=JsonUtility.ToJson(new Report{capturedAtUtc=DateTime.UtcNow.ToString("O"),checks=checks.ToArray(),errors=errors.ToArray()},true);
            File.WriteAllText(Path.Combine(Folder,"acceptance.json"),LastReport);
            Status=checks.All(c=>c.passed)&&errors.Count==0?"passed":"failed";session.SuppressResultFiles=false;Busy=false;
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
