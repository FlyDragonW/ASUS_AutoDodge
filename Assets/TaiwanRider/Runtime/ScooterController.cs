using UnityEngine;
using UnityEngine.InputSystem;
namespace TaiwanRider
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ScooterController : MonoBehaviour
    {
        public ScooterDefinition definition;
        public WheelCollider frontWheel, rearWheel;
        public Transform visualBody, frontSteering, frontVisualWheel, rearVisualWheel;
        public Transform cameraRig, needle;
        public Camera riderCamera;
        public ScenarioRunner session;
        public IScooterInput InputOverride { get; set; }
        public IScooterInput AutomaticInput { get; set; }
        public float SpeedKph => Body.linearVelocity.magnitude * 3.6f;
        public Rigidbody Body { get; private set; }
        public RiderCrash Crash { get; private set; }
        public bool HasCrashed => Crash && Crash.IsActive;
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Steer { get; private set; }
        public bool cameraMotion = true;
        Quaternion cameraHome, steeringHome, frontHome, rearHome, needleHome;
        Vector3 cameraPosition;
        Vector3 lastRideVelocity;
        float lookYaw, lookPitch, wheelSpin, steeringAngle;
        AudioSource engine;
        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = definition.massWithRider;
            Body.centerOfMass = new Vector3(0, .38f, -.04f);
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Keyboard-assisted riding uses a ground-contact chassis. Keep wheel
            // components as metric authoring references, with their solver disabled.
            frontWheel.enabled=rearWheel.enabled=false;
            cameraHome = cameraRig.localRotation; cameraPosition = cameraRig.localPosition;
            if(frontSteering) steeringHome=frontSteering.localRotation;
            if(frontVisualWheel) frontHome=frontVisualWheel.localRotation;
            if(rearVisualWheel) rearHome=rearVisualWheel.localRotation;
            if(needle) needleHome=needle.localRotation;
            engine=GetComponent<AudioSource>();
            Crash=GetComponent<RiderCrash>();if(!Crash)Crash=gameObject.AddComponent<RiderCrash>();Crash.Initialize(this);
        }
        void Update()
        {
            bool active = session && session.IsRunning && !session.IsPaused;
            var keyboard=Keyboard.current;
            var controls=InputOverride ?? (session && session.DemoMode?AutomaticInput:null);
            float t=0,b=0,s=0;
            if(active)
            {
                if(controls!=null){t=controls.Throttle;b=controls.Brake;s=controls.Steer;}
                else if(keyboard!=null)
                {
                    t=keyboard.wKey.isPressed?1:0;b=keyboard.sKey.isPressed?1:0;
                    s=(keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0);
                }
            }
            Throttle=Mathf.MoveTowards(Throttle,Mathf.Clamp01(t),Time.deltaTime*3f);
            Brake=Mathf.MoveTowards(Brake,Mathf.Clamp01(b),Time.deltaTime*7f);
            Steer=Mathf.MoveTowards(Steer,Mathf.Clamp(s,-1,1),Time.deltaTime*3.5f);
            // Looking around is independent of whether the rider or autopilot supplies driving input.
            if(active && Mouse.current!=null && Cursor.lockState==CursorLockMode.Locked && InputOverride==null && (!session.Demo || !session.Demo.ThirdPerson))
            {
                Vector2 delta=Mouse.current.delta.ReadValue();
                lookYaw=Mathf.Clamp(lookYaw+delta.x*.09f,-68,68);
                lookPitch=Mathf.Clamp(lookPitch-delta.y*.07f,-27,24);
                if(keyboard!=null && keyboard.spaceKey.wasPressedThisFrame) lookYaw=lookPitch=0;
            }
            if(engine){engine.pitch=Mathf.Min(2.5f,.72f+SpeedKph*.026f+Throttle*.32f);engine.volume=HasCrashed?0:active?.055f+Throttle*.09f:.035f;}
        }
        void FixedUpdate()
        {
            if(!session || !session.IsRunning){frontWheel.brakeTorque=rearWheel.brakeTorque=600;rearWheel.motorTorque=0;return;}
            steeringAngle=Steer*Mathf.Lerp(definition.steeringDegrees,10f,SpeedKph/definition.topSpeedKph);
            // Assisted bicycle model with a grounded rigidbody chassis.
            rearWheel.motorTorque=0;frontWheel.brakeTorque=rearWheel.brakeTorque=0;
            if(Physics.Raycast(Body.position+Vector3.up*.45f,Vector3.down,out var ground,.65f,~(1<<8),QueryTriggerInteraction.Ignore) && ground.normal.y>.65f)
            {
                float speed=Mathf.Max(0,Vector3.Dot(Body.linearVelocity,transform.forward));
                float acceleration=Throttle*definition.motorTorque/(definition.wheelRadius*definition.massWithRider);
                float resistance=.12f+speed*speed*.002f;
                float deceleration=Brake*6.8f+resistance;
                speed=Mathf.Clamp(speed+(acceleration-deceleration)*Time.fixedDeltaTime,0,definition.topSpeedKph/3.6f);
                float yaw=speed/definition.wheelbase*Mathf.Tan(steeringAngle*Mathf.Deg2Rad)*Mathf.Rad2Deg*Time.fixedDeltaTime;
                Quaternion heading=Quaternion.Euler(0,Body.rotation.eulerAngles.y+yaw,0);
                Vector3 forward=heading*Vector3.forward;
                Body.linearVelocity=new Vector3(forward.x*speed,Body.linearVelocity.y,forward.z*speed);
                Body.MoveRotation(heading);
            }
            if(SpeedKph<.45f && Throttle<.02f){Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;}
            lastRideVelocity=Body.linearVelocity;
        }
        void LateUpdate()
        {
            if(HasCrashed)return;
            float lean=-Steer*Mathf.Clamp01(SpeedKph/22f)*definition.visualLeanDegrees;
            visualBody.localRotation=Quaternion.Slerp(visualBody.localRotation,Quaternion.Euler(Brake*2.3f,0,lean),Time.deltaTime*6f);
            if(frontSteering) frontSteering.localRotation=steeringHome*Quaternion.Euler(0,0,steeringAngle);
            wheelSpin+=SpeedKph/3.6f/definition.wheelRadius*Mathf.Rad2Deg*Time.deltaTime;
            if(frontVisualWheel) frontVisualWheel.localRotation=Quaternion.Euler(0,0,steeringAngle)*frontHome*Quaternion.Euler(wheelSpin,0,0);
            if(rearVisualWheel) rearVisualWheel.localRotation=rearHome*Quaternion.Euler(wheelSpin,0,0);
            if(needle) needle.localRotation=needleHome*Quaternion.Euler(0,0,Mathf.Lerp(105,-105,SpeedKph/100f));
            float motion=cameraMotion?Mathf.Sin(Time.time*19)*Mathf.Clamp01(SpeedKph/30)*.0016f:0;
            cameraRig.localPosition=cameraPosition+Vector3.up*motion;
            cameraRig.localRotation=cameraHome*Quaternion.Euler(lookPitch+(cameraMotion?Brake*.65f:0),lookYaw,cameraMotion?lean*.075f:0);
        }
        public void ResetVehicle(Vector3 position, Quaternion rotation)
        {
            if(Crash)Crash.ResetCrash();lastRideVelocity=Vector3.zero;
            transform.SetPositionAndRotation(position,rotation);
            Body.position=position;Body.rotation=rotation;
            Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;
            Body.constraints=RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ;
            frontWheel.motorTorque=rearWheel.motorTorque=0;steeringAngle=0;
            frontWheel.brakeTorque=rearWheel.brakeTorque=600;
            Throttle=Brake=Steer=lookYaw=lookPitch=wheelSpin=0;
            visualBody.localRotation=Quaternion.identity;
            cameraRig.localPosition=cameraPosition;cameraRig.localRotation=cameraHome;
        }
        public void LaunchRider(Vector3 impactNormal,Collider struckCollider=null)
        {
            if(HasCrashed)return;
            Vector3 velocity=lastRideVelocity.sqrMagnitude>Body.linearVelocity.sqrMagnitude?lastRideVelocity:Body.linearVelocity;
            Crash.Launch(velocity,impactNormal,struckCollider);
        }
        void OnCollisionEnter(Collision hit)
        {
            if(!session || !session.IsRunning || Mathf.Max(SpeedKph,hit.relativeVelocity.magnitude*3.6f)<1.5f) return;
            if(hit.collider.gameObject.layer==10 || (hit.contacts.Length>0 && Mathf.Abs(hit.contacts[0].normal.y)<.6f))
            {
                LaunchRider(hit.contactCount>0?hit.GetContact(0).normal:-transform.forward,hit.collider);
                session.Finish(true,hit.collider.transform.root.name);
            }
        }
    }
}
