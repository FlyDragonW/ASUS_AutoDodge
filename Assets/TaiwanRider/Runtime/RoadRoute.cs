using UnityEngine;
namespace TaiwanRider
{
    public sealed class RoadRoute : MonoBehaviour
    {
        public Vector3[] points;
        public float CenterX(float z)
        {
            if(points==null || points.Length==0)return 0;
            for(int i=1;i<points.Length;i++)if(z<=points[i].z)
                return Mathf.Lerp(points[i-1].x,points[i].x,Mathf.InverseLerp(points[i-1].z,points[i].z,z));
            return points[points.Length-1].x;
        }
        public Vector3 Position(float z,float offset=0,float height=0)=>new Vector3(CenterX(z)+offset,height,z);
        public Quaternion Heading(float z)=>Quaternion.LookRotation(Position(z+1)-Position(z-1));
    }
}
