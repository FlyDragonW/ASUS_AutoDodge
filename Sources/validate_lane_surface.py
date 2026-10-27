"""Independent geometry checks for the staged visual lane algorithm; no Unity execution."""
import hashlib,json,math,pathlib
HERE=pathlib.Path(__file__).resolve().parent
WORK=HERE.parents[1]
survey_path=WORK/'UnityImplementation/Assets/TaiwanRider/Data/RouteSurvey.json'
source_path=HERE.parent/'Assets/TaiwanRider/Editor/SiteLaneSetup.cs'
source=json.loads(survey_path.read_text(encoding='utf-8-sig'))['lanePoints']
points=[]
for a,b in zip(source,source[1:]):
 if b['x']<=5.5:continue
 if not points:
  t=(5.5-a['x'])/(b['x']-a['x'])
  points.append((5.5,a['z']+(b['z']-a['z'])*t))
 points.append((b['x'],b['z']))
def normalized(v):
 length=math.hypot(*v)
 return tuple(x/length for x in v)
def minus(a,b):return (a[0]-b[0],a[1]-b[1])
vertices=[]; widths=[]
for i,p in enumerate(points):
 previous=normalized(minus(points[1],p)) if i==0 else normalized(minus(p,points[i-1]))
 following=previous if i==len(points)-1 else normalized(minus(points[i+1],p))
 before=(-previous[1],previous[0]);after=(-following[1],following[0])
 normal=normalized((before[0]+after[0],before[1]+after[1]))
 alignment=sum(a*b for a,b in zip(normal,after))
 offset=tuple(c*4.5/alignment for c in normal)
 vertices.extend([(p[0]-offset[0],p[1]-offset[1]),(p[0]+offset[0],p[1]+offset[1])])
 widths.append({'point':p,'miterAlignment':alignment,'normalWidthToNextSegment':2*sum(a*b for a,b in zip(offset,after))})
triangles=[]
for i in range(len(points)-1):
 v=i*2;triangles.extend([(v,v+1,v+2),(v+1,v+3,v+2)])
up_areas=[]
for ia,ib,ic in triangles:
 a,b,c=vertices[ia],vertices[ib],vertices[ic]
 ab=minus(b,a);ac=minus(c,a)
 up_areas.append((ab[1]*ac[0]-ab[0]*ac[1])/2)
hazard_x=13.05
for a,b in zip(source,source[1:]):
 if a['x']<=hazard_x<=b['x']:
  dx=b['x']-a['x'];dz=b['z']-a['z']
  cross=abs(dx*(0-a['z'])-dz*(hazard_x-a['x']))/math.hypot(dx,dz)
  hazard_distance=cross
report={'testKind':'Independent mathematics and source inspection; not a Unity compile or scene test',
 'points':points,'vertices':len(vertices),'triangles':len(triangles),'upFacingTriangleAreas':up_areas,
 'widthChecks':widths,'hazardStartPosition':[13.05,0],'hazardDistanceFromOSMPolylineMetres':hazard_distance,
 'checks':{'allTrianglesFaceUp':all(a>0 for a in up_areas),'widthIsNineMetresNormalToSegments':all(abs(w['normalWidthToNextSegment']-9)<1e-6 for w in widths),
 'noSharpMiter':all(w['miterAlignment']>.8 for w in widths),'hazardStartInsideVisualLane':hazard_distance<4.5,
 'endsAtCachedOSMNode':points[-1]==(source[-1]['x'],source[-1]['z']),
 'newScriptContainsNoColliderMutation':'AddComponent<MeshCollider>' not in source_path.read_text(encoding='utf-8') and 'GetComponent<Collider>().' not in source_path.read_text(encoding='utf-8')},
 'sourceHashes':{str(p.relative_to(WORK)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [survey_path,source_path]}}
(HERE/'site-lane-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'checks':report['checks'],'triangles':len(triangles),'hazardDistance':hazard_distance},indent=2))
if not all(report['checks'].values()):raise SystemExit(1)
