using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaiwanRider
{
    [DefaultExecutionOrder(300)]
    public sealed class RadarDisplay : MonoBehaviour
    {
        public RiderDemo demo;
        public bool Visible=true;
        readonly List<Vector3> vertices=new List<Vector3>(2500);
        readonly List<Color> colors=new List<Color>(2500);
        readonly List<int> indices=new List<int>(4000);
        Mesh mesh;
        MeshRenderer meshRenderer;
        public int RenderedBeamCount {get;private set;}
        readonly Color clear=new Color(.12f,.85f,1f,.34f),hit=new Color(1f,.62f,.1f,.8f),danger=new Color(1f,.17f,.1f,1);
        void Awake()
        {
            mesh=new Mesh{name="Live radar rays"};mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            meshRenderer=gameObject.AddComponent<MeshRenderer>();meshRenderer.sharedMaterial=Resources.Load<Material>("RiderRadar");
            meshRenderer.shadowCastingMode=ShadowCastingMode.Off;meshRenderer.receiveShadows=false;gameObject.layer=2;
        }
        void Line(Vector3 a,Vector3 b,float width,Color color)
        {
            Vector3 side=Vector3.Cross(b-a,demo.ActiveCamera.transform.forward).normalized*width*.5f;
            int i=vertices.Count;vertices.Add(a-side);vertices.Add(a+side);vertices.Add(b-side);vertices.Add(b+side);
            for(int j=0;j<4;j++)colors.Add(color);
            indices.Add(i);indices.Add(i+1);indices.Add(i+2);indices.Add(i+2);indices.Add(i+1);indices.Add(i+3);
        }
        void LateUpdate()
        {
            bool show=demo && demo.session.DemoMode && Visible && !demo.session.rider.HasCrashed;
            meshRenderer.enabled=show;if(!show){RenderedBeamCount=0;return;}
            vertices.Clear();colors.Clear();indices.Clear();RenderedBeamCount=0;
            foreach(var beam in demo.Sensor.Beams)
            {
                if(beam.end==Vector3.zero)continue;
                bool threat=beam.hit && beam.hit==demo.Pilot.Threat;
                Color color=threat?danger:beam.hit?hit:clear;
                Line(beam.origin,beam.end,threat?.038f:.018f,color);RenderedBeamCount++;
                if(beam.hit)
                {
                    Line(beam.end-Vector3.up*.09f,beam.end+Vector3.up*.09f,.035f,color);
                    Line(beam.end-Vector3.right*.09f,beam.end+Vector3.right*.09f,.035f,color);
                }
            }
            Vector3 center=demo.session.rider.transform.position+Vector3.up*.08f;
            for(int ring=1;ring<=2;ring++)for(int step=0;step<64;step++)
            {
                float a=step*Mathf.PI/32,b=(step+1)*Mathf.PI/32,radius=ring*2.5f;
                Line(center+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*radius,center+new Vector3(Mathf.Sin(b),0,Mathf.Cos(b))*radius,.027f,new Color(.15f,.8f,1,.48f));
            }
            var rider=demo.session.rider;float z=rider.transform.position.z;
            float current=rider.transform.position.x-demo.session.route.CenterX(z);
            Vector3 last=rider.transform.position+Vector3.up*.09f;
            for(int step=1;step<=24;step++)
            {
                float distance=step*.7f;
                float offset=Mathf.Lerp(current,demo.Pilot.TargetOffset,Mathf.SmoothStep(0,1,distance/8));
                Vector3 next=demo.session.route.Position(z+distance,offset,.09f);
                Line(last,next,.075f,demo.Pilot.State==AvoidanceState.Cruise?new Color(.25f,1,.7f,.9f):new Color(1,.72f,.15f,1));last=next;
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        }
        void OnDestroy(){if(mesh)Destroy(mesh);}
    }
}
