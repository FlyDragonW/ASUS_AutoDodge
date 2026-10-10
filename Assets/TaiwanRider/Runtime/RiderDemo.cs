using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace TaiwanRider
{
    [DefaultExecutionOrder(200)]
    public sealed class RiderDemo : MonoBehaviour
    {
        public ScenarioRunner session;
        public RadarSensor Sensor {get;private set;}
        public AutomaticAvoidance Pilot {get;private set;}
        public RadarDisplay Display {get;private set;}
        public Camera ViewCamera {get;private set;}
        public Camera ActiveCamera => ThirdPerson?ViewCamera:session.rider.riderCamera;
        public bool ThirdPerson {get;private set;}
        Transform avatar;
        Vector3 cameraVelocity;
        bool cameraSnap=true;
        Material jacket,helmet,visor;
        GUIStyle title,body;
        public void Initialize(ScenarioRunner runner)
        {
            session=runner;var rider=session.rider;
            Sensor=rider.gameObject.AddComponent<RadarSensor>();Sensor.session=session;Sensor.rider=rider;
            Pilot=rider.gameObject.AddComponent<AutomaticAvoidance>();Pilot.session=session;Pilot.radar=Sensor;Pilot.rider=rider;
            rider.AutomaticInput=Pilot;
            var cameraObject=new GameObject("Demo chase camera");ViewCamera=cameraObject.AddComponent<Camera>();ViewCamera.CopyFrom(rider.riderCamera);
            ViewCamera.nearClipPlane=.12f;ViewCamera.fieldOfView=62;
            var data=ViewCamera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Display=new GameObject("Raycast radar visualization").AddComponent<RadarDisplay>();Display.demo=this;
            ThirdPerson=session.ThirdPersonView;Display.Visible=session.RadarVisualization;
            MakeAvatar();SetEnabled(session.DemoMode);ResetForRun();
            var acceptance=gameObject.AddComponent<DemoAcceptance>();acceptance.demo=this;
        }
        Material Surface(string name,Color color,float smoothness)
        {
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",smoothness);return material;
        }
        Transform Part(string name,PrimitiveType kind,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(kind);go.name=name;go.layer=8;go.transform.SetParent(avatar,false);go.transform.localPosition=position;go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
        }
        void Limb(string name,Vector3 a,Vector3 b,float width,Material material)
        {
            var part=Part(name,PrimitiveType.Capsule,(a+b)*.5f,new Vector3(width,(b-a).magnitude*.5f,width),material);part.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }
        void MakeAvatar()
        {
            avatar=new GameObject("Demo rider body").transform;avatar.SetParent(session.rider.visualBody,false);avatar.gameObject.layer=8;
            jacket=Surface("Demo textile jacket",new Color(.09f,.17f,.20f),.15f);helmet=Surface("Demo safety helmet",new Color(.76f,.81f,.78f),.5f);visor=Surface("Demo dark visor",new Color(.025f,.055f,.065f),.7f);
            var torso=Part("Rider jacket",PrimitiveType.Capsule,new Vector3(0,1.02f,-.34f),new Vector3(.39f,.31f,.27f),jacket);torso.localRotation=Quaternion.Euler(12,0,0);
            Part("Helmet",PrimitiveType.Sphere,new Vector3(0,1.49f,-.25f),new Vector3(.32f,.34f,.35f),helmet);
            Part("Visor",PrimitiveType.Sphere,new Vector3(0,1.50f,-.10f),new Vector3(.285f,.15f,.09f),visor);
            foreach(int side in new[]{-1,1})
            {
                Limb("Upper trouser leg",new Vector3(side*.15f,.73f,-.36f),new Vector3(side*.20f,.58f,.13f),.145f,jacket);
                Limb("Lower trouser leg",new Vector3(side*.20f,.58f,.13f),new Vector3(side*.17f,.25f,.23f),.13f,jacket);
                Part("Boot",PrimitiveType.Sphere,new Vector3(side*.17f,.20f,.31f),new Vector3(.15f,.12f,.31f),visor);
            }
        }
        public void SetEnabled(bool value)
        {
            session.DemoMode=value;Pilot.ResetController();Sensor.ResetTracks();
            RefreshCursor();
        }
        public void SetThirdPerson(bool value){ThirdPerson=session.ThirdPersonView=value;cameraSnap=true;RefreshCameras();RefreshCursor();}
        public void SetRadarVisible(bool value){Display.Visible=session.RadarVisualization=value;}
        public void RefreshCursor()
        {
            bool free=!session.IsRunning || session.IsPaused || ThirdPerson;
            Cursor.lockState=free?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=free;
        }
        void RefreshCameras(){if(!ViewCamera)return;ViewCamera.enabled=ThirdPerson;session.rider.riderCamera.enabled=!ThirdPerson;}
        public void ResetForRun()
        {
            // Reattach before the crash rigidbody gets destroyed on retry.
            if(avatar){avatar.SetParent(session.rider.visualBody,false);avatar.localPosition=Vector3.zero;avatar.localRotation=Quaternion.identity;}
            Pilot.ResetController();Sensor.ResetTracks();cameraVelocity=Vector3.zero;cameraSnap=true;
            RefreshCameras();
        }
        void Update()
        {
            var keyboard=Keyboard.current;if(keyboard==null)return;
            if(keyboard.f2Key.wasPressedThisFrame)SetEnabled(!session.DemoMode);
            if(keyboard.vKey.wasPressedThisFrame)SetThirdPerson(!ThirdPerson);
            if(keyboard.gKey.wasPressedThisFrame)SetRadarVisible(!Display.Visible);
        }
        void LateUpdate()
        {
            if(!session)return;var rider=session.rider;
            avatar.gameObject.SetActive(ThirdPerson);
            if(rider.HasCrashed && rider.Crash.RiderBody && avatar.parent!=rider.Crash.RiderBody.transform)
                avatar.SetParent(rider.Crash.RiderBody.transform,true);
            if(!ThirdPerson)return;
            Vector3 focus=rider.HasCrashed && rider.Crash.RiderBody?rider.Crash.RiderBody.position:rider.transform.position+Vector3.up*.9f;
            Quaternion heading=session.route.Heading(rider.transform.position.z);
            Vector3 desired=focus+heading*new Vector3(-3.2f,5.4f,-7.8f);
            // Camera collision prevents the follow rig from entering shop walls.
            if(Physics.SphereCast(focus,.23f,(desired-focus).normalized,out var hit,Vector3.Distance(focus,desired),~(1<<8),QueryTriggerInteraction.Ignore))desired=hit.point+hit.normal*.3f;
            if(cameraSnap){ViewCamera.transform.position=desired;cameraSnap=false;}
            else ViewCamera.transform.position=Vector3.SmoothDamp(ViewCamera.transform.position,desired,ref cameraVelocity,.14f,100,Time.deltaTime);
            Vector3 look=focus+heading*Vector3.forward*3f;
            ViewCamera.transform.rotation=Quaternion.LookRotation(look-ViewCamera.transform.position,Vector3.up);
        }
        void OnGUI()
        {
            if(!session || !session.ShowUI || !session.DemoMode || !session.IsRunning || session.IsPaused)return;
            if(title==null)
            {
                title=new GUIStyle(GUI.skin.label){font=session.font,fontSize=22,fontStyle=FontStyle.Bold,normal={textColor=Color.white}};
                body=new GUIStyle(GUI.skin.label){font=session.font,fontSize=14,normal={textColor=new Color(.78f,.89f,.92f)}};
            }
            float scale=Screen.height/900f;GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);float x=Screen.width/scale-340;
            GUI.color=new Color(.025f,.05f,.065f,.9f);GUI.DrawTexture(new Rect(x,25,310,199),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.Label(new Rect(x+18,36,280,30),"AUTO AVOID  /  自動避閃",title);
            GUI.color=Pilot.State==AvoidanceState.Cruise?new Color(.32f,1,.77f):new Color(1,.69f,.23f);
            GUI.Label(new Rect(x+18,74,280,33),Pilot.StatusText,title);GUI.color=Color.white;
            GUI.Label(new Rect(x+18,114,280,25),$"模擬雷達 360°  ·  {RadarSensor.BeamCount} 射線  ·  32 m",body);
            string risk=float.IsInfinity(Pilot.TimeToCollision)?"—":Pilot.TimeToCollision.ToString("F2")+" s";
            GUI.Label(new Rect(x+18,142,285,28),$"碰撞預估 {risk}    煞車 {Pilot.Brake*100:F0}%",body);
            GUI.Label(new Rect(x+18,179,285,28),$"V {(ThirdPerson?"第三人稱":"第一人稱")}    G 射線{(Display.Visible?"開":"關")} · 偵測中",body);
            if(Display.Visible)GUI.Label(new Rect(x+18,229,295,28),"青：射線　橙：障礙物　紅：碰撞風險",body);
        }
        void OnDestroy()
        {
            if(ViewCamera)Destroy(ViewCamera.gameObject);if(Display)Destroy(Display.gameObject);if(avatar)Destroy(avatar.gameObject);
            if(jacket)Destroy(jacket);if(helmet)Destroy(helmet);if(visor)Destroy(visor);
        }
    }
}
