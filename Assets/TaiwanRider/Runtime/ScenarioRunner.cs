using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace TaiwanRider
{
    [DefaultExecutionOrder(-400)]
    public sealed class ScenarioRunner : MonoBehaviour
    {
        public ScooterController rider;
        public ScenarioDefinition[] scenarios;
        public HazardActor car, scooter;
        public Font font;
        public RoadRoute route;
        public Vector3 startPosition=new Vector3(2.65f,.04f,-76);
        public float finishZ=76;
        public int selectedScenario;
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public RunResult Result { get; private set; }
        public bool ShowUI=true;
        public bool DemoMode=true;
        public bool ThirdPersonView=false;
        public bool RadarVisualization=true;
        public RiderDemo Demo {get;private set;}
        public bool SuppressResultFiles;
        public float elapsed;
        public int PhysicsStep { get; private set; }
        public int EventStartedTick { get; private set; } = -1;
        public Vector3 EventRiderPosition { get; private set; }
        public float EventStartedAt { get; private set; }=-1;
        public string ResultDirectory => Path.Combine(Application.persistentDataPath,"Runs");
        HazardActor currentActor;
        float baselineSteer, baselineBrake;
        GUIStyle title, body, label, big, button, small;
        Texture2D panel, accent;
        void Awake(){Application.targetFrameRate=60;QualitySettings.vSyncCount=0;}
        void Start(){car.Initialize();scooter.Initialize();Demo=gameObject.AddComponent<RiderDemo>();Demo.Initialize(this);ResetToMenu();}
        public void SetDemoMode(bool value){DemoMode=value;if(Demo)Demo.SetEnabled(value);}
        void Update()
        {
            var k=Keyboard.current;
            if(k!=null)
            {
                if(k.escapeKey.wasPressedThisFrame && IsRunning)TogglePause();
                if(k.rKey.wasPressedThisFrame)StartRun(selectedScenario);
                if(k.enterKey.wasPressedThisFrame && !IsRunning)StartRun(selectedScenario);
                if(!IsRunning){if(k.digit1Key.wasPressedThisFrame)selectedScenario=0;if(k.digit2Key.wasPressedThisFrame)selectedScenario=1;if(k.digit3Key.wasPressedThisFrame)selectedScenario=2;}
            }
        }
        // Scenario trigger and run clock share the 50 Hz physics timeline.
        // Order: scenario (-400), actor (-300), radar (-200), pilot (-150), rider (0).
        void FixedUpdate()
        {
            if(!IsRunning || IsPaused)return;
            PhysicsStep++;elapsed=(float)(PhysicsStep*(double)Time.fixedDeltaTime);Result.elapsedSeconds=elapsed;
            Result.peakSpeedKph=Mathf.Max(Result.peakSpeedKph,rider.SpeedKph);
            if(Demo && Demo.Pilot.IsControlling){Result.autoAvoidEnabled=true;Result.avoidanceInterventions=Demo.Pilot.InterventionCount;}
            var scenario=scenarios[selectedScenario];
            if(currentActor && currentActor.enabled && EventStartedAt<0)
            {
                float distance=scenario.conflictZ-rider.Body.position.z;
                if(distance>0 && rider.SpeedKph>5 && distance/(rider.SpeedKph/3.6f)<scenario.triggerTimeToArrival)
                {currentActor.active=true;EventStartedAt=elapsed;EventStartedTick=PhysicsStep;EventRiderPosition=rider.Body.position;Result.hazardTriggered=true;baselineSteer=rider.Steer;baselineBrake=rider.Brake;}
            }
            if(EventStartedAt>=0 && Result.reactionSeconds<0 && (rider.Brake>Mathf.Max(.18f,baselineBrake+.12f) || Mathf.Abs(rider.Steer-baselineSteer)>.2f))
                Result.reactionSeconds=elapsed-EventStartedAt;
            if(currentActor && EventStartedAt>=0)
            {
                Collider playerCollider=rider.GetComponent<Collider>();
                Vector3 near=currentActor.bodyCollider.ClosestPoint(playerCollider.bounds.center);
                float clearance=Vector3.Distance(near,playerCollider.ClosestPoint(near));
                if(Result.minimumClearanceMetres<0 || clearance<Result.minimumClearanceMetres)Result.minimumClearanceMetres=clearance;
            }
            if(rider.transform.position.z>=finishZ)Finish(false,null);
            else if(Mathf.Abs(rider.transform.position.x)>24 || rider.transform.position.y<-3 || rider.transform.position.z<-91)Finish(true,"離開路線");
        }
        public void StartRun(int index)
        {
            if(Demo)Demo.ResetForRun();
            Time.timeScale=1;IsPaused=false;selectedScenario=Mathf.Clamp(index,0,scenarios.Length-1);
            car.ResetActor();scooter.ResetActor();
            var definition=scenarios[selectedScenario];
            car.gameObject.SetActive(definition.kind==ScenarioKind.CarCutIn);
            scooter.gameObject.SetActive(definition.kind==ScenarioKind.ScooterCrossing);
            currentActor=definition.kind==ScenarioKind.CarCutIn?car:definition.kind==ScenarioKind.ScooterCrossing?scooter:null;
            rider.ResetVehicle(startPosition,route?route.Heading(startPosition.z):Quaternion.identity);
            Result=new RunResult{scenario=definition.kind.ToString(),seed=definition.seed,autoAvoidEnabled=DemoMode && rider.InputOverride==null};
            elapsed=0;PhysicsStep=0;EventStartedAt=-1;EventStartedTick=-1;EventRiderPosition=startPosition;IsRunning=true;
            Physics.SyncTransforms();
            if(Demo)Demo.RefreshCursor();
        }
        public void ResetToMenu()
        {
            if(Demo)Demo.ResetForRun();
            Time.timeScale=1;IsRunning=IsPaused=false;car.ResetActor();scooter.ResetActor();
            car.gameObject.SetActive(false);scooter.gameObject.SetActive(false);
            rider.ResetVehicle(startPosition,route?route.Heading(startPosition.z):Quaternion.identity);
            Result=null;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        public void TogglePause()
        {
            IsPaused=!IsPaused;Time.timeScale=IsPaused?0:1;
            if(Demo)Demo.RefreshCursor();
        }
        public void Finish(bool collision,string hit)
        {
            if(!IsRunning)return;
            IsRunning=false;Time.timeScale=1;IsPaused=false;
            Result.collision=collision;Result.finished=!collision;Result.hitObject=hit;
            Result.endedAtUtc=DateTime.UtcNow.ToString("O");
            if(collision)Result.minimumClearanceMetres=0;
            car.active=scooter.active=false;
            if(collision){if(!rider.HasCrashed)rider.LaunchRider(-rider.transform.forward);}
            else {rider.Body.linearVelocity=Vector3.zero;rider.Body.angularVelocity=Vector3.zero;}
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(!SuppressResultFiles)
            {
                try{Directory.CreateDirectory(ResultDirectory);File.WriteAllText(Path.Combine(ResultDirectory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"),JsonUtility.ToJson(Result,true));}
                catch(Exception ex){Debug.LogWarning("Unable to save local run result: "+ex.Message);}
            }
        }
        void SetupUI()
        {
            if(title!=null)return;
            panel=new Texture2D(1,1);panel.SetPixel(0,0,new Color(.035f,.065f,.077f,.93f));panel.Apply();
            accent=new Texture2D(1,1);accent.SetPixel(0,0,new Color(.39f,.88f,.75f));accent.Apply();
            title=new GUIStyle(GUI.skin.label){font=font,fontSize=38,fontStyle=FontStyle.Bold,normal={textColor=Color.white}};
            body=new GUIStyle(GUI.skin.label){font=font,fontSize=17,wordWrap=true,normal={textColor=new Color(.83f,.87f,.86f)}};
            label=new GUIStyle(body){fontSize=13};small=new GUIStyle(body){fontSize=11};
            big=new GUIStyle(title){fontSize=44};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=16,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(16,10,6,6)};
        }
        void OnGUI()
        {
            if(!ShowUI)return;SetupUI();GUI.skin.font=font;
            float scale=Screen.height/900f;GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            float width=Screen.width/scale;
            GUI.DrawTexture(new Rect(28,25,4,44),accent);
            GUI.Label(new Rect(44,22,530,26),"公館  /  GONGGUAN",body);
            GUI.Label(new Rect(44,49,640,24),"汀州路三段・羅斯福路四段90巷    /    RIDING STUDY 01",small);
            if(rider.HasCrashed && rider.Crash.Age<2.6f)
            {
                GUI.color=new Color(1,.16f,.08f,Mathf.Clamp01(1-rider.Crash.Age/.4f)*.45f);
                GUI.DrawTexture(new Rect(0,0,width,900),Texture2D.whiteTexture);GUI.color=Color.white;
                GUI.Label(new Rect(35,795,550,55),"碰撞！",title);
                GUI.Label(new Rect(38,850,550,26),"R 立即重試",body);
                return;
            }
            if(IsRunning && !IsPaused)
            {
                GUI.Label(new Rect(35,779,170,65),Mathf.RoundToInt(rider.SpeedKph).ToString("00"),big);
                GUI.Label(new Rect(120,811,70,25),"km/h",label);
                GUI.Label(new Rect(36,852,1000,25),DemoMode?"F2 手動接管    V 第一／第三人稱    G 射線開關    滑鼠 環視 / 空白鍵 回正    R 重播    ESC 設定":"W 油門    S 煞車    A / D 轉向    V 視角    G 射線開關    F2 自動避閃    R 重試    ESC 設定",small);
                GUI.Label(new Rect(width-260,813,220,25),scenarios[selectedScenario].title,label);
                float p=Mathf.InverseLerp(startPosition.z,finishZ,rider.transform.position.z);
                GUI.DrawTexture(new Rect(width-260,848,220,3),panel);GUI.DrawTexture(new Rect(width-260,848,220*p,3),accent);
                return;
            }
            Rect box=new Rect(35,155,460,648);GUI.DrawTexture(box,panel);
            GUI.Label(new Rect(62,180,410,52),IsPaused?"稍作停留":Result!=null?(Result.collision?"再試一次":"平安抵達"):"從公館出發",title);
            GUI.Label(new Rect(65,242,395,55),"一台 125，一段熟悉的街道。\n留意巷口，為下一個變化保留空間。",body);
            if(Result!=null)
            {
                string reaction=Result.reactionSeconds<0?"未反應 / 不適用":Result.reactionSeconds.ToString("F2")+" 秒";
                string distance=Result.minimumClearanceMetres<0?"—":Result.minimumClearanceMetres.ToString("F2")+" m";
                GUI.Label(new Rect(65,304,395,62),$"反應  {reaction}    最近間距  {distance}\n用時  {Result.elapsedSeconds:F1} 秒    最高  {Result.peakSpeedKph:F0} km/h",label);
            }
            else GUI.Label(new Rect(65,307,392,50),"固定日間場景  ·  約 160 m\n簡化操控  ·  公開參考與原創重建",label);
            for(int i=0;i<scenarios.Length;i++)
            {
                GUI.backgroundColor=i==selectedScenario?new Color(.4f,.82f,.68f):new Color(.36f,.43f,.45f);
                GUI.enabled=!IsPaused;
                if(GUI.Button(new Rect(65,382+i*49,397,42),$"0{i+1}   {scenarios[i].title}",button))selectedScenario=i;
                GUI.enabled=true;
            }
            GUI.backgroundColor=Color.white;
            GUI.Label(new Rect(65,536,393,43),scenarios[selectedScenario].description,small);
            bool requestedDemo=GUI.Toggle(new Rect(65,579,397,27),DemoMode," 自動避閃（F2）");
            if(requestedDemo!=DemoMode)SetDemoMode(requestedDemo);
            if(Demo)
            {
                GUI.backgroundColor=!Demo.ThirdPerson?new Color(.4f,.82f,.68f):new Color(.36f,.43f,.45f);
                if(GUI.Button(new Rect(65,610,192,32),"第一人稱",button))Demo.SetThirdPerson(false);
                GUI.backgroundColor=Demo.ThirdPerson?new Color(.4f,.82f,.68f):new Color(.36f,.43f,.45f);
                if(GUI.Button(new Rect(270,610,192,32),"第三人稱",button))Demo.SetThirdPerson(true);
                GUI.backgroundColor=Color.white;
                bool rays=GUI.Toggle(new Rect(65,650,397,26),Demo.Display.Visible," 顯示雷達射線（G，關閉仍持續偵測）");
                if(rays!=Demo.Display.Visible)Demo.SetRadarVisible(rays);
            }
            rider.cameraMotion=GUI.Toggle(new Rect(65,682,310,25),rider.cameraMotion," 輕微騎乘鏡頭動態");
            GUI.Label(new Rect(65,711,100,25),"視野 FOV",small);
            Camera viewedCamera=Demo?Demo.ActiveCamera:rider.riderCamera;
            viewedCamera.fieldOfView=GUI.HorizontalSlider(new Rect(166,720,230,18),viewedCamera.fieldOfView,55,75);
            GUI.backgroundColor=new Color(.45f,.92f,.76f);
            if(GUI.Button(new Rect(65,754,397,43),IsPaused?"繼續騎乘   →":DemoMode?"開始自動避閃示範   →":"開始騎乘   →",button)){if(IsPaused)TogglePause();else StartRun(selectedScenario);}
            GUI.backgroundColor=Color.white;
            GUI.Label(new Rect(38,847,width-60,30),"Street geometry: © OpenStreetMap contributors · Materials: Poly Haven CC0 · Font: Noto Sans TC / OFL · 街景細節含推估",small);
        }
        void OnDestroy(){Time.timeScale=1;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;if(panel)Destroy(panel);if(accent)Destroy(accent);}
    }
}
