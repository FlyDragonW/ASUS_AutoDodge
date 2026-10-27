using UnityEngine;
namespace TaiwanRider
{
    /// <summary>Visual wheel motion from actual displacement, including every LOD.</summary>
    public sealed class TrafficWheelMotion : MonoBehaviour
    {
        public Transform[] wheels;
        public float radius=.217f;
        Quaternion[] homes;
        float[] angles;
        Vector3 previous;
        void OnEnable()
        {
            previous=transform.position;
            if(wheels==null)return;
            if(homes==null || homes.Length!=wheels.Length)
            {
                homes=new Quaternion[wheels.Length];angles=new float[wheels.Length];
                for(int i=0;i<wheels.Length;i++)homes[i]=wheels[i]?wheels[i].localRotation:Quaternion.identity;
            }
        }
        void LateUpdate()
        {
            Vector3 movement=transform.position-previous;previous=transform.position;
            if(homes==null || wheels==null || movement.sqrMagnitude>9f || Time.deltaTime<=0)return;
            for(int i=0;i<wheels.Length;i++)
            {
                var wheel=wheels[i];if(!wheel)continue;
                Vector3 axle=wheel.parent.TransformDirection(homes[i]*Vector3.right);
                angles[i]=Mathf.Repeat(angles[i]+Vector3.Dot(movement,Vector3.Cross(axle,Vector3.up))*Mathf.Rad2Deg/Mathf.Max(radius,.01f),360);
                wheel.localRotation=homes[i]*Quaternion.AngleAxis(angles[i],Vector3.right);
            }
        }
    }
}
