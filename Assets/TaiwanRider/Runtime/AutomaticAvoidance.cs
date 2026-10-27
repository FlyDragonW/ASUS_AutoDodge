using UnityEngine;
using System.Collections.Generic;

namespace TaiwanRider
{
    public enum AvoidanceState {Cruise,Brake,EvadeLeft,EvadeRight,Recover,EmergencyStop}
    [DefaultExecutionOrder(-150)]
    public sealed class AutomaticAvoidance : MonoBehaviour,IScooterInput
    {
        public ScenarioRunner session;
        public RadarSensor radar;
        public RiderVisionSensor vision;
        public ScooterController rider;
        public float cruiseSpeedKph=40;
        // Non-serialized experimental override. Zero preserves the normal fusion policy.
        [System.NonSerialized] public float ValidationHorizonOverride;
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
        public int VisionAssistedInterventions {get;private set;}
        public int VisionExtendedRiskSteps {get;private set;}
        public string LastDecisionSource {get;private set;}="Raycast";
        public string LastVisualClass {get;private set;}="";
        public float LastVisualConfidence {get;private set;}
        public float LastVisualAgeMs {get;private set;}
        public float LastInterventionTtc {get;private set;}
        public bool LastBaselineWouldReact {get;private set;}
        public bool IsControlling => session && session.DemoMode && rider.InputOverride==null;
        float stateSince,lastRiskAt,lastThreatSeen,clearCorridorSince=-1;
        Collider evadeTarget;
        bool dynamicManeuver;
        float dynamicBrakeDemand=1;
        static readonly float[] EscapeOffsets={.6f,4f};
        Vector3 egoHalfSize=new Vector3(.52f,0,1f);
        float lastDynamicPlan=-1;
        struct Motion { public Vector3 velocity, acceleration; public float time; }
        readonly Dictionary<Collider,Motion> motion=new Dictionary<Collider,Motion>();
        void ObserveMotion()
        {
            foreach(var contact in radar.Contacts)
            {
                var next=new Motion{velocity=contact.velocity,time=contact.seenAt};
                if(motion.TryGetValue(contact.collider,out var previous))
                {
                    float dt=next.time-previous.time;
                    if(dt<=.001f)continue;
                    if(dt<.25f)next.acceleration=Vector3.Lerp(previous.acceleration,Vector3.ClampMagnitude((next.velocity-previous.velocity)/dt,30f),.65f);
                }
                motion[contact.collider]=next;
            }
        }
        Vector3 ForecastVelocity(RadarSensor.Contact contact)
        {
            var velocity=contact.velocity;
            if(motion.TryGetValue(contact.collider,out var track) && Mathf.Abs(velocity.x)>.1f && track.acceleration.x*velocity.x>0)
                velocity.x+=Mathf.Clamp(track.acceleration.x,-20,20)*.25f;
            return velocity;
        }
        public string ManeuverReason { get; private set; } = "";
        struct DynamicPath { public bool clear; public float clearance, firstHit; }
        // Short-horizon bicycle rollout, using the same actuator slew/deceleration
        // as ScooterController. Only measured radar tracks enter this planner.
        DynamicPath EvaluateDynamicPath(float targetOffset,float brakeDemand=1)
        {
            const float step=.04f;
            Vector3 position=rider.Body.position;
            float yaw=rider.Body.rotation.eulerAngles.y*Mathf.Deg2Rad;
            float speed=Mathf.Max(0,Vector3.Dot(rider.Body.linearVelocity,rider.transform.forward));
            float duration=Mathf.Max(2.0f,speed/6.8f+.4f);
            float brake=rider.Brake,throttle=rider.Throttle,steer=rider.Steer;
            var path=new DynamicPath{clear=true,clearance=999,firstHit=duration};
            for(float t=step;t<=duration+.001f;t+=step)
            {
                float kph=speed*3.6f;
                float lookAhead=2.2f+speed*.04f;
                Vector3 target=session.route.Position(position.z+lookAhead,targetOffset,position.y);
                Vector3 forward=new Vector3(Mathf.Sin(yaw),0,Mathf.Cos(yaw));
                float angle=Vector3.SignedAngle(forward,target-position,Vector3.up)*Mathf.Deg2Rad;
                float wheelAngle=Mathf.Atan2(2*rider.definition.wheelbase*Mathf.Sin(angle),lookAhead)*Mathf.Rad2Deg;
                float available=Mathf.Lerp(rider.definition.steeringDegrees,10f,kph/rider.definition.topSpeedKph);
                float command=Mathf.Clamp(wheelAngle/available,-.8f,.8f);
                steer=Mathf.MoveTowards(steer,command,step*3.5f);
                float feedForward=(.12f+speed*speed*.002f)*rider.definition.wheelRadius*rider.definition.massWithRider/rider.definition.motorTorque;
                float throttleDemand=brakeDemand>.01f?0:Mathf.Clamp01(feedForward+(cruiseSpeedKph-kph)*.22f);
                throttle=Mathf.MoveTowards(throttle,throttleDemand,step*3f);brake=Mathf.MoveTowards(brake,brakeDemand,step*7f);
                float acceleration=throttle*rider.definition.motorTorque/(rider.definition.wheelRadius*rider.definition.massWithRider);
                speed=Mathf.Max(0,speed+(acceleration-brake*6.8f-.12f-speed*speed*.002f)*step);
                yaw+=speed/rider.definition.wheelbase*Mathf.Tan(steer*available*Mathf.Deg2Rad)*step;
                position+=new Vector3(Mathf.Sin(yaw),0,Mathf.Cos(yaw))*speed*step;
                // Rotated ego envelope plus 15 cm planning margin. No scenario IDs,
                // actor paths, future transforms or ground-truth velocities are read.
                float rx=Mathf.Abs(Mathf.Cos(yaw))*egoHalfSize.x+Mathf.Abs(Mathf.Sin(yaw))*egoHalfSize.z+.15f;
                float rz=Mathf.Abs(Mathf.Sin(yaw))*egoHalfSize.x+Mathf.Abs(Mathf.Cos(yaw))*egoHalfSize.z+.15f;
                foreach(var contact in radar.Contacts)
                {
                    // The first velocity estimate is filtered and may lag a new
                    // motion. Test a bounded speed interval, not only its mean.
                    for(int sample=0;sample<3;sample++)
                    {
                        float scale=.75f+sample*.25f;
                        Vector3 obstacle=contact.position+ForecastVelocity(contact)*(t*scale);
                        float dx=Mathf.Abs(obstacle.x-position.x)-contact.extents.x-rx;
                        float dz=Mathf.Abs(obstacle.z-position.z)-contact.extents.z-rz;
                        float separation=Mathf.Max(dx,dz);
                        path.clearance=Mathf.Min(path.clearance,separation);
                        if(separation<0 && path.clear){path.clear=false;path.firstHit=t;}
                    }
                }
            }
            return path;
        }
        void PlanDynamicResponse(RadarSensor.Contact risk)
        {
            float center=rider.transform.position.x-session.route.CenterX(rider.transform.position.z);
            float bestOffset=center;
            var best=EvaluateDynamicPath(bestOffset);
            // Prefer yielding in the current corridor whenever full braking is clear.
            // Only steer under braking if the straight braking path remains occupied.
            if(!best.clear)
            {
                foreach(float offset in EscapeOffsets)
                {
                    if(!SideAvailable(offset,risk))continue;
                    var candidate=EvaluateDynamicPath(offset);
                    if((candidate.clear && !best.clear) ||
                        (candidate.clear==best.clear && (candidate.clear?candidate.clearance>best.clearance:
                        candidate.firstHit>best.firstHit+.02f || (Mathf.Abs(candidate.firstHit-best.firstHit)<.02f && candidate.clearance>best.clearance))))
                    {best=candidate;bestOffset=offset;}
                }
            }
            float selectedBrake=1;
            // Braking can trap a rider alongside an intruding vehicle. Consider
            // maintaining cruise only when the complete measured-traffic rollout
            // is clear; never accelerate above the configured cruise speed.
            if(!best.clear)
            {
                foreach(float offset in new[]{center,.6f,4f})
                {
                    if(!SideAvailable(offset,risk))continue;
                    var candidate=EvaluateDynamicPath(offset,0);
                    if(candidate.clear && (!best.clear || candidate.clearance>best.clearance))
                    {best=candidate;bestOffset=offset;selectedBrake=0;}
                }
            }
            // Preserve an already commanded escape briefly when every rollout
            // is unsafe. Oscillating between lane targets consumes steering slew.
            // A newly clear route or a static obstruction can still override it.
            if(dynamicManeuver && Mathf.Abs(TargetOffset-center)>.3f && dynamicBrakeDemand>.99f && !best.clear && Time.fixedTime-stateSince<.45f && SideAvailable(TargetOffset,risk))
            {bestOffset=TargetOffset;best=EvaluateDynamicPath(bestOffset);}
            TargetOffset=bestOffset;dynamicManeuver=true;dynamicBrakeDemand=selectedBrake;
            State=AvoidanceState.Brake;
            ManeuverReason=best.clear?(selectedBrake<.01f?"Maintain cruise through predicted gap":Mathf.Abs(bestOffset-center)<.1f?"Yield with full braking":"Brake and steer through predicted gap"):
                "No collision-free rollout; full braking with best predicted clearance";
        }
        public string StatusText => State==AvoidanceState.Cruise?"沿車道巡航":State==AvoidanceState.Brake?(dynamicManeuver && dynamicBrakeDemand<.01f?"動態避讓":"煞車讓行"):State==AvoidanceState.EvadeLeft?"向左避讓":State==AvoidanceState.EvadeRight?"向右避讓":State==AvoidanceState.Recover?"回到原車道":"緊急煞停";
        public void ResetController()
        {
            Throttle=Brake=Steer=0;State=AvoidanceState.Cruise;TargetOffset=2.65f;
            Threat=evadeTarget=null;TimeToCollision=float.PositiveInfinity;InterventionCount=0;FirstInterventionAt=-1;
            stateSince=lastRiskAt=lastThreatSeen=Time.fixedTime;
            var chassis=rider.GetComponent<BoxCollider>();
            if(chassis)egoHalfSize=Vector3.Scale(chassis.size*.5f,rider.transform.lossyScale)+Vector3.Scale(new Vector3(Mathf.Abs(chassis.center.x),0,Mathf.Abs(chassis.center.z)),rider.transform.lossyScale);
            motion.Clear();clearCorridorSince=-1;dynamicManeuver=false;lastDynamicPlan=-1;ManeuverReason="";
            VisionAssistedInterventions=VisionExtendedRiskSteps=0;LastDecisionSource="Raycast";LastVisualClass="";LastVisualConfidence=LastVisualAgeMs=0;
        }
        static bool Slab(float position,float velocity,float radius,ref float entry,ref float exit)
        {
            if(Mathf.Abs(velocity)<.01f)return Mathf.Abs(position)<=radius;
            float a=(-radius-position)/velocity,b=(radius-position)/velocity;
            entry=Mathf.Max(entry,Mathf.Min(a,b));exit=Mathf.Min(exit,Mathf.Max(a,b));return entry<=exit;
        }
        bool PredictedCollision(RadarSensor.Contact contact,out float ttc,float horizon=1.8f)
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
            Vector3 v=frame*(ForecastVelocity(contact)-rider.Body.linearVelocity);
            float entry=0,exit=horizon;
            if(p.z<-contact.extents.z-1.5f)return false;
            if(!Slab(p.x,v.x,contact.extents.x+.52f,ref entry,ref exit))return false;
            if(!Slab(p.z,v.z,contact.extents.z+1.0f,ref entry,ref exit))return false;
            ttc=entry;return entry<=exit && exit>=0 && entry<horizon;
        }
        bool SideAvailable(float offset,RadarSensor.Contact current)
        {
            float z=rider.transform.position.z;
            // Check the destination corridor through the obstacle's far edge.
            // A fixed 12 m centre-point window missed adjacent parked cars when
            // intervention began earlier than that window, especially at 40 km/h.
            float endZ=Mathf.Max(z+12f,z+rider.SpeedKph/3.6f*2.4f+4f);
            if(current!=null)endZ=Mathf.Max(endZ,current.position.z+current.extents.z+3f);
            foreach(var contact in radar.Contacts)
            {
                if(contact.velocity.sqrMagnitude>=.36f)
                {
                    // Keep the existing short prediction for moving traffic.
                    // Extrapolating a merging car's transient lateral velocity
                    // over 2.4 s can incorrectly block a successful escape lane.
                    if(contact==current)continue;
                    Vector3 prediction=contact.position+contact.velocity*.6f;
                    float side=prediction.x-session.route.CenterX(prediction.z);
                    if(prediction.z>z-2f && prediction.z<z+12f && Mathf.Abs(side-offset)<contact.extents.x+.6f)return false;
                    continue;
                }
                // Static obstructions include the current threat. A candidate
                // corridor must clear its full bounds through the far edge.
                float staticSide=contact.position.x-session.route.CenterX(contact.position.z);
                if(contact.position.z+contact.extents.z>z-2f && contact.position.z-contact.extents.z<endZ &&
                    Mathf.Abs(staticSide-offset)<contact.extents.x+.6f)return false;
            }
            return true;
        }
        void EnterEmergencyStop()
        {
            State=AvoidanceState.EmergencyStop;
            // Brake on the present corridor instead of steering toward a blocked side.
            TargetOffset=rider.transform.position.x-session.route.CenterX(rider.transform.position.z);
            stateSince=Time.fixedTime;clearCorridorSince=-1;
        }
        bool ForwardCorridorBlocked()
        {
            float z=rider.transform.position.z;
            // Use intended cruise speed even at rest. TTC alone becomes infinite
            // at zero speed while the physical obstruction still occupies the lane.
            float range=Mathf.Clamp(cruiseSpeedKph/3.6f*2.4f+4f,12f,32f);
            foreach(var contact in radar.Contacts)
            {
                Vector3 p=contact.position;
                float side=p.x-session.route.CenterX(p.z);
                if(p.z+contact.extents.z>z-1f && p.z-contact.extents.z<z+range &&
                    Mathf.Abs(side-TargetOffset)<contact.extents.x+.6f)return true;
            }
            return false;
        }
        void BeginIntervention(RadarSensor.Contact contact)
        {
            LastBaselineWouldReact=PredictedCollision(contact,out _);
            LastInterventionTtc=TimeToCollision;LastDecisionSource=ValidationHorizonOverride>0?"Fixed horizon Raycast":"Raycast";LastVisualClass="";LastVisualConfidence=LastVisualAgeMs=0;
            if(ValidationHorizonOverride<=0 && vision && vision.TryGetEvidence(contact.collider,out var evidence))
            {
                LastVisualClass=evidence.detection.ClassName;LastVisualConfidence=evidence.detection.confidence;
                LastVisualAgeMs=(float)((Time.realtimeSinceStartupAsDouble-evidence.capturedRealtime)*1000);
                LastDecisionSource=LastBaselineWouldReact?"Raycast + vision confirmation":"Vision + raycast early intervention";
                if(!LastBaselineWouldReact)VisionAssistedInterventions++;
            }
            if(State==AvoidanceState.Cruise || State==AvoidanceState.Recover)
            {
                InterventionCount++;if(FirstInterventionAt<0){FirstInterventionAt=session.elapsed;FirstInterventionZ=rider.transform.position.z;}
            }
            dynamicManeuver=false;
            // Moving traffic uses receding-horizon trajectory checks; static
            // obstacles retain the established side-clearance and stop-hold policy.
            if(contact.velocity.sqrMagnitude>=.36f)PlanDynamicResponse(contact);
            else if(SideAvailable(.6f,contact)){State=AvoidanceState.EvadeLeft;TargetOffset=.6f;}
            else if(SideAvailable(4f,contact)){State=AvoidanceState.EvadeRight;TargetOffset=4f;}
            else EnterEmergencyStop();
            stateSince=Time.fixedTime;evadeTarget=contact.collider;lastThreatSeen=stateSince;
        }
        void FixedUpdate()
        {
            if(!IsControlling || !session.IsRunning || session.IsPaused || rider.HasCrashed){Throttle=Brake=Steer=0;return;}
            ObserveMotion();
            float now=Time.fixedTime;RadarSensor.Contact risk=null;TimeToCollision=float.PositiveInfinity;
            foreach(var contact in radar.Contacts)
                if(PredictedCollision(contact,out float ttc,ValidationHorizonOverride>0?ValidationHorizonOverride:(vision?vision.Horizon(contact.collider):1.8f)) && ttc<TimeToCollision){TimeToCollision=ttc;risk=contact;}
            if(ValidationHorizonOverride<=0 && risk!=null && !PredictedCollision(risk,out _))VisionExtendedRiskSteps++;
            Threat=risk!=null?risk.collider:evadeTarget;
            if(risk!=null)
            {
                lastRiskAt=now;
                if(State==AvoidanceState.Cruise || State==AvoidanceState.Recover)BeginIntervention(risk);
            }
            // Re-evaluate evolving cut-ins while braking; never wait for a
            // frame update or an ended intervention to refresh the trajectory.
            if(dynamicManeuver && State==AvoidanceState.Brake && risk!=null && risk.velocity.sqrMagnitude>=.36f && now-lastDynamicPlan>=.04f)
            {PlanDynamicResponse(risk);lastDynamicPlan=now;}
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
            else if(State==AvoidanceState.EmergencyStop)
            {
                if(ForwardCorridorBlocked() || risk!=null)clearCorridorSince=-1;
                else
                {
                    if(clearCorridorSince<0)clearCorridorSince=now;
                    if(now-clearCorridorSince>=.75f && now-stateSince>.8f)
                    {State=AvoidanceState.Recover;stateSince=now;evadeTarget=Threat=null;clearCorridorSince=-1;}
                }
            }
            else if(State==AvoidanceState.Brake && now-lastRiskAt>.65f && now-stateSince>.8f)
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
            if(State==AvoidanceState.Brake && dynamicManeuver){Brake=dynamicBrakeDemand;if(Brake>.01f)Throttle=0;}
            else if(State==AvoidanceState.Brake || State==AvoidanceState.EmergencyStop){Throttle=0;Brake=1;}
            float lookAhead=(State==AvoidanceState.Brake && dynamicManeuver)?2.2f+speed*.04f:evading?4.4f+speed*.06f:6f+speed*.35f;
            Vector3 target=session.route.Position(rider.transform.position.z+lookAhead,TargetOffset,rider.transform.position.y);
            float angle=Vector3.SignedAngle(rider.transform.forward,target-rider.transform.position,Vector3.up)*Mathf.Deg2Rad;
            float wheelAngle=Mathf.Atan2(2*rider.definition.wheelbase*Mathf.Sin(angle),lookAhead)*Mathf.Rad2Deg;
            float available=Mathf.Lerp(rider.definition.steeringDegrees,10f,rider.SpeedKph/rider.definition.topSpeedKph);
            Steer=Mathf.Clamp(wheelAngle/available,-.8f,.8f);
        }
    }
}
