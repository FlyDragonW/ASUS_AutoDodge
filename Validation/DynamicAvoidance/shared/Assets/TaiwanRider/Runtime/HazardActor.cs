using UnityEngine;
namespace TaiwanRider
{
    [DefaultExecutionOrder(-300)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class HazardActor : MonoBehaviour
    {
        public ScenarioKind kind;
        public bool active;
        public Collider bodyCollider;
        Rigidbody body;
        float elapsed;
        Vector3 initial;
        Quaternion heading;
        public Vector3 ModelForward
        {
            get
            {
                Transform front=null,rear=null;
                // Only the installed model defines direction; disabled backups do not.
                var model=transform.Find(kind==ScenarioKind.CarCutIn?"Sedan":"Scooter125_LOD");
                foreach(var part in (model?model:transform).GetComponentsInChildren<Transform>(true))
                {
                    if(part.name==(kind==ScenarioKind.CarCutIn?"Front grille":"FrontWheel"))front=part;
                    if(part.name==(kind==ScenarioKind.CarCutIn?"Rear plate":"RearWheel"))rear=part;
                }
                return front && rear?(front.position-rear.position).normalized:transform.forward;
            }
        }
        public void AlignModelForward()
        {
            var model=transform.Find(kind==ScenarioKind.CarCutIn?"Sedan":"Scooter125_LOD");
            if(!model)return;
            // Compare inside the actor's frame, never against world Z: crossing
            // actors already face sideways before their FBX is instantiated.
            Vector3 localForward=transform.InverseTransformDirection(ModelForward);
            float correction=-Mathf.Atan2(localForward.x,localForward.z)*Mathf.Rad2Deg;
            model.localRotation=Quaternion.AngleAxis(correction,Vector3.up)*model.localRotation;
        }
        public void Initialize()
        {
            AlignModelForward();
            body=GetComponent<Rigidbody>();body.isKinematic=true;
            body.interpolation=RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            initial=transform.position;heading=transform.rotation;
        }
        public void ResetActor()
        {
            if(!body)Initialize();active=false;elapsed=0;
            transform.SetPositionAndRotation(initial,heading);
            body.position=initial;body.rotation=heading;
        }
        void FixedUpdate()
        {
            if(!active)return;
            elapsed+=Time.fixedDeltaTime;
            Vector3 next;
            if(kind==ScenarioKind.CarCutIn)
            {
                float u=Mathf.Clamp01(elapsed/.7f);
                next=initial+new Vector3(-2.55f*Mathf.SmoothStep(0,1,u),0,elapsed*4.5f);
            }
            else next=initial+Vector3.left*(elapsed*11.5f);
            Vector3 delta=next-body.position;
            if(delta.sqrMagnitude>.00001f)body.MoveRotation(Quaternion.Slerp(body.rotation,Quaternion.LookRotation(delta),Time.fixedDeltaTime*4));
            body.MovePosition(next);
            if(elapsed>10f)active=false;
        }
    }
}
