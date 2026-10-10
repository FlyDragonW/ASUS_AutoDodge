using System.Collections.Generic;
using UnityEngine;

namespace TaiwanRider
{
    /// <summary>Physical first-person ejection, isolated from assisted riding.</summary>
    public sealed class RiderCrash : MonoBehaviour
    {
        struct Attachment
        {
            public Transform part,parent;
            public Vector3 position;
            public Quaternion rotation;
        }
        readonly List<Attachment> attachments=new List<Attachment>();
        ScooterController rider;
        GameObject crashRoot;
        Collider riderCollider,impactCollider;
        float startedAt,originalLinearDamping,originalAngularDamping;
        public bool IsActive {get;private set;}
        public Rigidbody RiderBody {get;private set;}
        public float Age => IsActive?Time.time-startedAt:0;

        public void Initialize(ScooterController controller)
        {
            rider=controller;
            originalLinearDamping=rider.Body.linearDamping;
            originalAngularDamping=rider.Body.angularDamping;
            Remember(rider.cameraRig);
            foreach(Transform part in rider.visualBody)
                if(part.name=="Jacket sleeve" || part.name=="Riding glove" || part.name=="Gloved finger")Remember(part);
        }
        void Remember(Transform part)
        {
            attachments.Add(new Attachment{part=part,parent=part.parent,position=part.localPosition,rotation=part.localRotation});
        }
        public void Launch(Vector3 incomingVelocity,Vector3 impactNormal,Collider struckCollider=null)
        {
            if(IsActive)return;
            IsActive=true;startedAt=Time.time;
            Vector3 forward=Vector3.ProjectOnPlane(incomingVelocity,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=rider.transform.forward;
            float speed=Vector3.ProjectOnPlane(incomingVelocity,Vector3.up).magnitude;
            Vector3 sideways=Vector3.ProjectOnPlane(impactNormal,Vector3.up);
            sideways-=forward*Vector3.Dot(sideways,forward);

            crashRoot=new GameObject("Ejected rider (runtime)");crashRoot.layer=8;
            crashRoot.transform.SetPositionAndRotation(rider.cameraRig.position-Vector3.up*.55f,Quaternion.LookRotation(forward));
            var capsule=crashRoot.AddComponent<CapsuleCollider>();capsule.height=1.35f;capsule.radius=.22f;
            riderCollider=capsule;impactCollider=struckCollider;
            RiderBody=crashRoot.AddComponent<Rigidbody>();RiderBody.mass=75;
            RiderBody.interpolation=RigidbodyInterpolation.Interpolate;
            RiderBody.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            RiderBody.linearDamping=.12f;RiderBody.angularDamping=.45f;RiderBody.maxAngularVelocity=6;
            foreach(var ownCollider in rider.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(capsule,ownCollider);
            // Briefly separate the rider from the exact impact surface so the
            // initial launch clears the bonnet instead of wedging in the car.
            if(impactCollider)Physics.IgnoreCollision(capsule,impactCollider);
            foreach(var attachment in attachments)attachment.part.SetParent(crashRoot.transform,true);
            RiderBody.linearVelocity=forward*Mathf.Clamp(speed*.78f,3f,22f)+Vector3.up*Mathf.Clamp(4f+speed*.14f,4f,8f)+sideways*.8f;
            RiderBody.angularVelocity=crashRoot.transform.right*3.4f+forward*1.3f;

            // The bike also loses its assisted upright constraint and tumbles.
            rider.Body.constraints=RigidbodyConstraints.None;
            rider.Body.linearDamping=.25f;rider.Body.angularDamping=.55f;
            rider.Body.linearVelocity=forward*Mathf.Clamp(speed*.3f,1f,8f)+Vector3.up*2f;
            rider.Body.angularVelocity=rider.transform.right*2.3f-forward*3.5f;
        }
        void FixedUpdate()
        {
            if(IsActive && impactCollider && Age>.25f)
            {
                Physics.IgnoreCollision(riderCollider,impactCollider,false);impactCollider=null;
            }
        }
        public void ResetCrash()
        {
            foreach(var attachment in attachments)
            {
                if(!attachment.part || !attachment.parent)continue;
                attachment.part.SetParent(attachment.parent,false);
                attachment.part.localPosition=attachment.position;attachment.part.localRotation=attachment.rotation;
            }
            if(crashRoot){crashRoot.SetActive(false);Destroy(crashRoot);}
            RiderBody=null;riderCollider=impactCollider=null;IsActive=false;
            if(rider && rider.Body){rider.Body.linearDamping=originalLinearDamping;rider.Body.angularDamping=originalAngularDamping;}
        }
        void OnDestroy(){if(crashRoot)Destroy(crashRoot);}
    }
}
