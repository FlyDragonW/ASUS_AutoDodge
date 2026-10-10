using UnityEngine;

namespace TaiwanRider
{
    public enum AvoidanceState {Cruise,Brake,EvadeLeft,EvadeRight,Recover,EmergencyStop}
    [DefaultExecutionOrder(-150)]
    public sealed class AutomaticAvoidance : MonoBehaviour,IScooterInput
    {
        public ScenarioRunner session;
        public RadarSensor radar;
        public ScooterController rider;
        public float cruiseSpeedKph=40;
        public float Throttle {get;private set;}
        public float Brake {get;private set;}
        public float Steer {get;private set;}
        public AvoidanceState State {get;private set;}
        public float TargetOffset {get;private set;}=2.65f;
        public float TimeToCollision {get;private set;}=float.PositiveInfinity;
        public Collider Threat {get;private set;}
        public int InterventionCount {get;private set;}
        public float FirstInterventionAt {get;private set;}=-1;
        public float FirstInterventionZ {get;private set;}
        public bool IsControlling => session && session.DemoMode && rider.InputOverride==null;
        float stateSince,lastRiskAt,lastThreatSeen;
        Collider evadeTarget;
        public string StatusText => State==AvoidanceState.Cruise?"沿車道巡航":State==AvoidanceState.Brake?"煞車讓行":State==AvoidanceState.EvadeLeft?"向左避讓":State==AvoidanceState.EvadeRight?"向右避讓":State==AvoidanceState.Recover?"回到原車道":"緊急煞停";
        public void ResetController()
        {
            Throttle=Brake=Steer=0;State=AvoidanceState.Cruise;TargetOffset=2.65f;
            Threat=evadeTarget=null;TimeToCollision=float.PositiveInfinity;InterventionCount=0;FirstInterventionAt=-1;
            stateSince=lastRiskAt=lastThreatSeen=Time.fixedTime;
        }
        static bool Slab(float position,float velocity,float radius,ref float entry,ref float exit)
        {
            if(Mathf.Abs(velocity)<.01f)return Mathf.Abs(position)<=radius;
            float a=(-radius-position)/velocity,b=(radius-position)/velocity;
            entry=Mathf.Max(entry,Mathf.Min(a,b));exit=Mathf.Min(exit,Mathf.Max(a,b));return entry<=exit;
        }
        bool PredictedCollision(RadarSensor.Contact contact,out float ttc)
        {
            ttc=float.PositiveInfinity;
            if(contact.velocity.sqrMagnitude<.36f)
            {
                // Recovery follows a curve back to the lane, rather than holding
                // its instantaneous diagonal heading into parked vehicles.
                float currentOffset=rider.transform.position.x-session.route.CenterX(rider.transform.position.z);
                float obstacleOffset=contact.position.x-session.route.CenterX(contact.position.z);
                float minOffset=Mathf.Min(currentOffset,TargetOffset)-.52f;
                float maxOffset=Mathf.Max(currentOffset,TargetOffset)+.52f;
                if(obstacleOffset+contact.extents.x<minOffset || obstacleOffset-contact.extents.x>maxOffset)return false;
            }
            Quaternion frame=Quaternion.Inverse(session.route.Heading(rider.transform.position.z));
            Vector3 p=frame*(contact.position-rider.transform.position);
            Vector3 v=frame*(contact.velocity-rider.Body.linearVelocity);
            float entry=0,exit=2f;
            if(p.z<-contact.extents.z-1.5f)return false;
            if(!Slab(p.x,v.x,contact.extents.x+.52f,ref entry,ref exit))return false;
            if(!Slab(p.z,v.z,contact.extents.z+1.0f,ref entry,ref exit))return false;
            ttc=entry;return entry<=exit && exit>=0 && entry<1.8f;
        }
        bool SideAvailable(float offset,RadarSensor.Contact current)
        {
            float z=rider.transform.position.z;
            foreach(var contact in radar.Contacts)
            {
                if(contact==current)continue;
                Vector3 prediction=contact.position+contact.velocity*.6f;
                float side=prediction.x-session.route.CenterX(prediction.z);
                if(prediction.z>z-2 && prediction.z<z+12 && Mathf.Abs(side-offset)<contact.extents.x+.6f)return false;
            }
            return true;
        }
        void BeginIntervention(RadarSensor.Contact contact)
        {
            if(State==AvoidanceState.Cruise || State==AvoidanceState.Recover)
            {
                InterventionCount++;if(FirstInterventionAt<0){FirstInterventionAt=session.elapsed;FirstInterventionZ=rider.transform.position.z;}
            }
            Vector3 localVelocity=Quaternion.Inverse(session.route.Heading(rider.transform.position.z))*contact.velocity;
            // A fast lateral crossing is allowed to pass in front. A merging or
            // stationary obstruction is passed on a raycast-observed clear side.
            if(Mathf.Abs(localVelocity.x)>Mathf.Abs(localVelocity.z)*1.8f && Mathf.Abs(localVelocity.x)>3)
            {State=AvoidanceState.Brake;TargetOffset=2.65f;}
            else if(SideAvailable(.6f,contact)){State=AvoidanceState.EvadeLeft;TargetOffset=.6f;}
            else if(SideAvailable(4f,contact)){State=AvoidanceState.EvadeRight;TargetOffset=4f;}
            else State=AvoidanceState.EmergencyStop;
            stateSince=Time.fixedTime;evadeTarget=contact.collider;lastThreatSeen=stateSince;
        }
        void FixedUpdate()
        {
            if(!IsControlling || !session.IsRunning || session.IsPaused || rider.HasCrashed){Throttle=Brake=Steer=0;return;}
            float now=Time.fixedTime;RadarSensor.Contact risk=null;TimeToCollision=float.PositiveInfinity;
            foreach(var contact in radar.Contacts)
                if(PredictedCollision(contact,out float ttc) && ttc<TimeToCollision){TimeToCollision=ttc;risk=contact;}
            Threat=risk!=null?risk.collider:evadeTarget;
            if(risk!=null)
            {
                lastRiskAt=now;
                if(State==AvoidanceState.Cruise || State==AvoidanceState.Recover)BeginIntervention(risk);
            }
            bool evading=State==AvoidanceState.EvadeLeft || State==AvoidanceState.EvadeRight;
            if(evading)
            {
                bool passed=false;
                foreach(var contact in radar.Contacts)if(contact.collider==evadeTarget)
                {
                    lastThreatSeen=now;
                    passed=contact.position.z+contact.extents.z<rider.transform.position.z-2f;
                }
                if(now-stateSince>1.2f && (passed || now-lastThreatSeen>1f) && risk==null){State=AvoidanceState.Recover;stateSince=now;evadeTarget=Threat=null;}
            }
            else if((State==AvoidanceState.Brake || State==AvoidanceState.EmergencyStop) && now-lastRiskAt>.65f && now-stateSince>.8f)
            {State=AvoidanceState.Recover;stateSince=now;evadeTarget=Threat=null;}
            if(State==AvoidanceState.Recover)
            {
                TargetOffset=Mathf.MoveTowards(TargetOffset,2.65f,Time.fixedDeltaTime*1.05f);
                float offset=rider.transform.position.x-session.route.CenterX(rider.transform.position.z);
                if(Mathf.Abs(offset-2.65f)<.22f && Mathf.Abs(TargetOffset-2.65f)<.01f)State=AvoidanceState.Cruise;
            }
            float speed=rider.SpeedKph/3.6f;
            float feedForward=(.12f+speed*speed*.002f)*rider.definition.wheelRadius*rider.definition.massWithRider/rider.definition.motorTorque;
            Throttle=Mathf.Clamp01(feedForward+(cruiseSpeedKph-rider.SpeedKph)*.22f);
            Brake=Mathf.Clamp01((rider.SpeedKph-cruiseSpeedKph)/8f);
            if(evading && now-stateSince<.85f){Throttle=0;Brake=.68f;}
            if(State==AvoidanceState.Brake || State==AvoidanceState.EmergencyStop){Throttle=0;Brake=1;}
            float lookAhead=evading?4.4f+speed*.06f:6f+speed*.35f;
            Vector3 target=session.route.Position(rider.transform.position.z+lookAhead,TargetOffset,rider.transform.position.y);
            float angle=Vector3.SignedAngle(rider.transform.forward,target-rider.transform.position,Vector3.up)*Mathf.Deg2Rad;
            float wheelAngle=Mathf.Atan2(2*rider.definition.wheelbase*Mathf.Sin(angle),lookAhead)*Mathf.Rad2Deg;
            float available=Mathf.Lerp(rider.definition.steeringDegrees,10f,rider.SpeedKph/rider.definition.topSpeedKph);
            Steer=Mathf.Clamp(wheelAngle/available,-.8f,.8f);
        }
    }
}
