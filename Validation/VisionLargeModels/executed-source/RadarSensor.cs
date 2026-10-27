using System.Collections.Generic;
using UnityEngine;

namespace TaiwanRider
{
    [DefaultExecutionOrder(-200)]
    public sealed class RadarSensor : MonoBehaviour
    {
        public struct Beam {public Vector3 origin,end;public Collider hit;}
        public sealed class Contact
        {
            public Collider collider;
            public Vector3 position,velocity,extents;
            public float seenAt;
        }
        readonly Dictionary<Collider,Contact> tracks=new Dictionary<Collider,Contact>();
        readonly HashSet<Collider> observed=new HashSet<Collider>();
        readonly List<Collider> expired=new List<Collider>();
        readonly List<Contact> contacts=new List<Contact>();
        // 68 casts instead of 113 (39.8% fewer), retaining denser forward coverage.
        public const int SurroundBeamCount=44,ForwardBeamCount=24;
        public const int BeamCount=SurroundBeamCount+ForwardBeamCount;
        public readonly Beam[] Beams=new Beam[BeamCount];
        public IReadOnlyList<Contact> Contacts => contacts;
        public int ScanCount {get;private set;}
        public ScenarioRunner session;
        public ScooterController rider;
        public Vector3 Origin => rider.transform.position+Vector3.up*.7f;
        public void ResetTracks(){tracks.Clear();contacts.Clear();System.Array.Clear(Beams,0,Beams.Length);ScanCount=0;}
        void FixedUpdate()
        {
            if(!session || !session.DemoMode || session.IsPaused || rider.HasCrashed)return;
            observed.Clear();float now=Time.fixedTime;
            for(int i=0;i<Beams.Length;i++)
            {
                float angle=i<SurroundBeamCount?i*(360f/SurroundBeamCount)-180f:(i-SurroundBeamCount)*(80f/(ForwardBeamCount-1))-40f;
                float range=Mathf.Abs(angle)<=60?32f:Mathf.Abs(angle)<=125?16f:10f;
                Vector3 direction=rider.transform.rotation*Quaternion.Euler(0,angle,0)*Vector3.forward;
                var beam=new Beam{origin=Origin,end=Origin+direction*range};
                if(Physics.Raycast(Origin,direction,out var hit,range,~(1<<8),QueryTriggerInteraction.Ignore))
                {
                    beam.hit=hit.collider;beam.end=hit.point;
                    if(observed.Add(hit.collider))
                    {
                        Bounds bounds=hit.collider.bounds;
                        if(!tracks.TryGetValue(hit.collider,out var track))
                        {
                            track=new Contact{collider=hit.collider,position=bounds.center,seenAt=now};tracks.Add(hit.collider,track);
                        }
                        float dt=now-track.seenAt;
                        if(dt>.001f && dt<.25f)
                        {
                            Vector3 measured=(bounds.center-track.position)/dt;measured.y=0;
                            track.velocity=Vector3.Lerp(track.velocity,Vector3.ClampMagnitude(measured,40f),.8f);
                        }
                        track.position=bounds.center;track.extents=bounds.extents;track.seenAt=now;
                    }
                }
                Beams[i]=beam;
            }
            contacts.Clear();expired.Clear();
            foreach(var pair in tracks)
            {
                if(!pair.Key || now-pair.Value.seenAt>.2f)expired.Add(pair.Key);
                else contacts.Add(pair.Value);
            }
            foreach(var key in expired)tracks.Remove(key);
            ScanCount++;
        }
    }
}
