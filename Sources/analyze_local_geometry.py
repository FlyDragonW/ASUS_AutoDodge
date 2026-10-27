"""Read-only provenance/geometry audit of the locally cached Gongguan reconstruction.
Does not contact external services or modify Unity/source assets. Outputs this folder's
geometry-audit.json. All geometric measurements refer to cached OSM, not a site survey.
"""
import json,math,pathlib,collections,hashlib
HERE=pathlib.Path(__file__).resolve().parent
WORK=HERE.parents[1]
BASE=WORK/'UnityImplementation'
def read(path):return json.loads((BASE/path).read_text(encoding='utf-8-sig'))
osm=read('Sources/gongguan-osm.json')
survey=read('Assets/TaiwanRider/Data/RouteSurvey.json')
layout=read('Sources/building-layout.json')
refs=read('Sources/References/credits.json')
main=next(e for e in osm['elements'] if e.get('tags',{}).get('name')=='汀州路三段')
lane=next(e for e in osm['elements'] if e.get('tags',{}).get('name')=='羅斯福路四段90巷')
origin=(survey['originLatitude'],survey['originLongitude'])
forward=(-.77865,.62747);right=(.62747,.77865)
def local(p):
 e=(p['lon']-origin[1])*111320*math.cos(math.radians(origin[0]));n=(p['lat']-origin[0])*111320
 return {'x':e*right[0]+n*right[1],'y':0.0,'z':e*forward[0]+n*forward[1]}
def bearing(e,n):return (math.degrees(math.atan2(e,n))+360)%360
def dist(a,b):return math.hypot(a['x']-b['x'],a['z']-b['z'])
def lerp_x(z):
 ps=survey['points']
 for a,b in zip(ps,ps[1:]):
  if z<=b['z']:
   t=max(0,min(1,(z-a['z'])/(b['z']-a['z'])))
   return a['x']+(b['x']-a['x'])*t
 return ps[-1]['x']
def lane_z(x):
 ps=survey['lanePoints']
 for a,b in zip(ps,ps[1:]):
  if x<=b['x']:
   t=max(0,min(1,(x-a['x'])/(b['x']-a['x'])))
   return a['z']+(b['z']-a['z'])*t
 return ps[-1]['z']
shared=sorted(set(main['nodes'])&set(lane['nodes']))
common=[]
for node in shared:
 a=main['geometry'][main['nodes'].index(node)];b=lane['geometry'][lane['nodes'].index(node)]
 common.append({'node':node,'mainIndex':main['nodes'].index(node),'laneIndex':lane['nodes'].index(node),'mainCoordinate':a,'laneCoordinate':b,'identical':a==b,'matchesOrigin':a=={'lat':origin[0],'lon':origin[1]}})
computed=sorted([local(p) for p in main['geometry']],key=lambda p:p['z'])
projection_error=max(dist(a,b) for a,b in zip(computed,survey['points']))
play_points=[{'x':lerp_x(-76),'z':-76}]+[p for p in survey['points'] if -76<p['z']<76]+[{'x':lerp_x(76),'z':76}]
play_length=sum(dist(a,b) for a,b in zip(play_points,play_points[1:]))
lane_points=[local(p) for p in lane['geometry']]
lane_length=sum(dist(a,b) for a,b in zip(lane_points,lane_points[1:]))
width_samples=[]
for z in [-120,-100,-76,-40,-7,0,20,76,120]:
 dx=lerp_x(z+1)-lerp_x(z-1);theta=math.atan2(dx,2)
 width_samples.append({'z':z,'routeYawDegrees':math.degrees(theta),'nominalXWidth':12.4,'actualPerpendicularWidth':12.4*math.cos(theta),'offsetLongitudinalDriftAt6_2m':6.2*math.sin(theta)})
sample_buildings=[]
for i in range(7):
 cx=20.5+i*7.8;lo=cx-3.85;hi=cx+3.85
 samples=[(lo+(hi-lo)*j/100,lane_z(lo+(hi-lo)*j/100)) for j in range(101)]
 crossing=[{'x':x,'z':z} for x,z in samples if -14.8<=z<=-6.7]
 if crossing:sample_buildings.append({'generatedAlleyBuildingIndex':i,'generatedCenter':{'x':cx,'z':-10.75},'footprintX':[lo,hi],'footprintZ':[-14.8,-6.7],'cachedOSMCentrelineOverlapSampleRange':[crossing[0],crossing[-1]]})
photo_info=[]
for ref in refs:
 meta=ref['metadata'];lat=meta.get('GPSLatitude',{}).get('value');lon=meta.get('GPSLongitude',{}).get('value')
 result={'file':ref['file'],'title':ref['title'],'source':ref['source'],'exactTargetCornerVerified':False,'captureDate':meta.get('DateTimeOriginal',{}).get('value')}
 if 'reference_0' in ref['file']:
  result['visualInspection']={'readableAddress':'羅斯福路四段78巷2弄2','targetLane90':False,'limitation':'Different named alley; cannot establish Lane 90 geometry or full building heights.'}
 if 'reference_3' in ref['file']:
  result['visualInspection']={'readableAddress':'汀州路三段185','coordinateVerified':False,'limitation':'Partial ground-floor facade behind a police SUV; address is a research anchor, not a measured footprint or height.'}
 if lat and lon:
  p=local({'lat':float(lat),'lon':float(lon)});result.update({'cachedMetadataCoordinate':{'lat':float(lat),'lon':float(lon)},'localPosition':p,'distanceFromJunctionMetres':math.hypot(p['x'],p['z'])})
 else:result['coordinateAvailable']=False
 photo_info.append(result)
heights=collections.Counter(round(b['height'],4) for b in layout);widths=collections.Counter(b['width'] for b in layout)
floor_counts=collections.Counter(round((b['height']-3.3)/2.9)+1 for b in layout)
same_height_run={}
for side in [-1,1]:
 row=sorted([b for b in layout if b['side']==side],key=lambda b:b['center'])
 best=[];current=[]
 for b in row:
  if current and abs(current[-1]['height']-b['height'])>.0001:current=[]
  current.append(b)
  if len(current)>len(best):best=current[:]
 same_height_run[str(side)]={'count':len(best),'height':best[0]['height'],'names':[b['name'] for b in best]}
report={
 'auditKind':'Local source/provenance audit; read-only; no live Unity inspection or current survey',
 'cachedOSMDate':osm['osm3s']['timestamp_osm_base'],
 'conclusion':{
  'namedGeographicJunctionMatchesCachedOSM':True,
  'fullSceneIsOneToOneSiteReconstruction':False,
  'summaryZh':'原點確實位於本地OSM的汀州路三段×羅斯福路四段90巷共同節點；僅道路中心線有位置依據。寬度、建物足跡與樓高、店名、開口、標線號誌、巷底與停車配置不是實景量測。',
  'mainMissingEvidence':'No acquired building footprints, cadastral frontage geometry, height/levels survey, measured road/sidewalk widths or complete dated photographic coverage of this exact corner.'},
 'junction':{'origin':{'latitude':origin[0],'longitude':origin[1]},'commonNodes':common,
  'mainWay':{'id':main['id'],'name':main['tags']['name'],'tags':main['tags'],'vertices':len(main['geometry'])},
  'laneWay':{'id':lane['id'],'name':lane['tags']['name'],'tags':lane['tags'],'vertices':len(lane['geometry'])},
  'namingCaution':'Intersecting road is Lane 90 of Section 4 Roosevelt Road; do not label this as the junction with the Roosevelt Road main carriageway.'},
 'sourceCoverage':{'osmElementCount':len(osm['elements']),'osmTypes':dict(collections.Counter(e['type'] for e in osm['elements'])),'buildingElements':sum('building' in e.get('tags',{}) for e in osm['elements']),
  'roadWidthTagsPresent':any('width' in e.get('tags',{}) for e in osm['elements']),
  'elevationTagsPresent':any('ele' in e.get('tags',{}) for e in osm['elements']),
  'referenceImages':photo_info},
 'coordinateAudit':{
  'source':'Tools/derive_route.py',
  'plusZGeographicBearingDegrees':bearing(*forward),'plusXGeographicBearingDegrees':bearing(*right),
  'plusZDescription':'northwest; player travels opposite the stored main-way node order',
  'plusXDescription':'northeast; same side as the cached Lane 90 branch',
  'projectionMaxDisagreementWithRouteSurveyMetres':projection_error,
  'widthMetres':survey['roadWidthMetres'],'widthIsEstimated':survey['widthIsEstimated'],
  'playableZRange':[-76,76],'playableZExtent':152,'cachedCentrelineLengthWithinPlayableRange':play_length,
  'currentRoadStripZRange':[-120,120],'horizontalOffsetInsteadOfNormalOffset':width_samples,
  'cachedLaneLengthMetres':lane_length,'lanePoints':survey['lanePoints'],
  'laneSegmentAnglesFromLocalPlusXDegrees':[math.degrees(math.atan2(b['z']-a['z'],b['x']-a['x'])) for a,b in zip(lane_points,lane_points[1:])]},
 'generatedBuildingAudit':{
  'count':len(layout),'sideCounts':dict(collections.Counter(str(b['side']) for b in layout)),
  'allFiftyFiveBuildingFootprintsAndHeightsAreGenerated':True,
  'generator':'Tools/model_assets.py environment(), lines 215-307',
  'randomSeed':125,'widthChoicesMetres':[6.4,7.2,8.0,9.4],
  'heightFormula':'3.3 + random.choice([3,4,4,5]) * 2.9; these are not observed storey counts',
  'widthDistribution':dict(widths),'heightDistribution':dict(heights),'impliedTotalStoreysDistribution':dict(floor_counts),
  'longestSameHeightRunBySide':same_height_run,
  'constantBodyDepthMetres':8.7,'constantBodyCentreLateralOffsetMetres':12.0,'constantUpperFacadeOffsetMetres':7.65,
  'constantGroundFloorHeightMetres':3.3,'constantUpperFloorRhythmMetres':2.9,
  'constantWindowCentreBaseHeightMetres':4.9,
  'constantArcadePillarOffsetMetres':7.76,
  'whyRowsLookUniformZh':'雖然不是每棟同高同寬，但只抽3種樓高/4種寬度，且全部8.7m深、同一立面線、同2.9m樓層節奏、同3.3m騎樓；沒有任何地籍或逐棟外觀對位。加隨機變化仍不能達成1:1。',
  'laterRefinementDoesNotFixEnvelope':'IntersectionDetailSetup.Storefront reuses building-layout height/width and the same 8.7 m depth; StreetLifeSetup adds fictional signs/props and material variation.',
  'alleyBackdropAdditionalBuildings':14,
  'alleyBackdropHeightPatternMetres':[10.2,13.0,15.8],
  'alleyBackdropTerminalBlock':{'center':[79,8.1,0],'dimensions':[6,16.2,31],'isMeasured':False}},
 'objectiveConflicts':[{
  'id':'lane_polyline_ignored','severity':'high','source':'RiderSceneBuilder.cs:20,131; IntersectionDetailSetup.cs:271-311',
  'evidence':'RouteSurvey.json has five Lane 90 points; RiderSceneBuilder Survey reads only points. Both road/visual alley are centred on z=0.',
  'cachedCentrelineZAtX32_5':lane_z(32.5),'cachedCentrelineZAtX76':lane_z(76),'cachedCentrelineZAtLastNode':lane_points[-1]['z'],
  'fixAvailableLocally':'Generate alley centreline/road mesh and its authoring frame from cached lanePoints; retain explicit estimated width until measured.'},
 {'id':'invented_alley_buildings_cover_osm_way','severity':'high','source':'IntersectionDetailSetup.cs:285-308',
  'evidence':'The straight generated negative-z alley row intersects the cached curved Lane 90 centreline at these samples.',
  'intersections':sample_buildings,
  'terminalBlockContainsLastOSMNode':76<=lane_points[-1]['x']<=82 and -15.5<=lane_points[-1]['z']<=15.5,
  'fixAvailableLocally':'Remove or move the invented backdrop out of the known lane polyline corridor. Do not create an unsupported dead-end facade; actual endpoint topology beyond the cache remains unknown.'},
 {'id':'global_x_offsets_are_not_metric_perpendicular_offsets','severity':'medium','source':'Runtime/RoadRoute.cs Position(); RiderSceneBuilder.cs RoadStrip()',
  'evidence':'Position(z,offset) adds offset in global X. Curved-road width measured normal to the tangent is 12.4*cos(yaw), with attached curb/building frames rotated separately.',
  'fixAvailableLocally':'Use the cached tangent and its perpendicular normal for authored road edges/kerbs; keep the chosen width marked estimated. Coordinate-controller assumptions require integration review.'},
 {'id':'all_building_dimensions_unverified','severity':'critical_for_1_to_1','source':'Tools/model_assets.py:229-305; Sources/building-layout.json',
  'evidence':'No building records exist in local OSM. Every frontage width, height, footprint and roofline is generated.',
  'fixAvailableLocally':False,'requiredEvidence':'Exact-corner photos/panoramas with correspondence to addresses, footprint/cadastral or orthophoto geometry, and independently justified heights. Do not replace uniform blocks with differently random blocks and call it a match.'},
 {'id':'road_furniture_and_markings_unverified','severity':'high_for_1_to_1','source':'RiderSceneBuilder.cs:142-200',
  'evidence':'Crosswalk, stop line, double yellow, red kerb, signals, lamps, drains and parking spaces are procedural placements. Local OSM has no node tags proving these at this corner.',
  'fixAvailableLocally':False,'requiredEvidence':'Location-specific visual survey before re-authoring their number, placement and dimensions.'},
 {'id':'traffic_rules_are_not_scene_dimensions','severity':'context','source':'Sources/gongguan-osm.json tags; Runtime/HazardActor.cs',
  'evidence':'Cached main road maxspeed=40 and one lane per direction. Cached Lane 90 oneway=yes follows its nodes from the junction northeastward, while the hazard drives from the alley back toward the main road.',
  'recommendation':'Keep user-authorized vehicle capability100km/h separate from mapped limit40. Label current opposing alley hazard as an intentional unsafe event if kept; do not silently treat it as compliant traffic.'}],
 'localOnlyRepairPriority':[
  {'priority':1,'action':'Use Lane 90 cached centreline and preserve the exact shared node as (0,0,0).','canBeObjectiveWithoutNewSurvey':True},
  {'priority':2,'action':'Remove the generated alley terminal wall and other geometry intersecting the known centreline; no new guessed buildings.','canBeObjectiveWithoutNewSurvey':True},
  {'priority':3,'action':'Make road/kerb offsets geometrically consistent with the cached tangent; retain unknown width/elevation status.','canBeObjectiveWithoutNewSurvey':True},
  {'priority':4,'action':'Do not rebuild 55 building envelopes as supposedly exact until independent corner evidence is available.','canBeObjectiveWithoutNewSurvey':False}],
 'evidenceFiles':[{'path':str((BASE/p).relative_to(WORK)),'sha256':hashlib.sha256((BASE/p).read_bytes()).hexdigest()} for p in ['Sources/gongguan-osm.json','Assets/TaiwanRider/Data/RouteSurvey.json','Tools/derive_route.py','Tools/model_assets.py','Sources/building-layout.json','Assets/TaiwanRider/Runtime/RoadRoute.cs','Assets/TaiwanRider/Editor/RiderSceneBuilder.cs','Assets/TaiwanRider/Editor/IntersectionDetailSetup.cs','Sources/intersection-detail-provenance.json','Sources/streetlife-provenance.json','Sources/References/credits.json']]
}
report['targetReferenceEpoch']='2025; selected by user. Current OSM, if acquired, would not by itself prove unchanged 2025 conditions.'
provenance_path=HERE/'site-osm-provenance.json'
if provenance_path.exists():
 provenance=json.loads(provenance_path.read_text(encoding='utf-8'))
 report['siteOSMExtension']={
  'status':'acquired' if provenance.get('success') else 'unavailable',
  'provenance':provenance,
  'buildingFootprintsAvailableFromExtension':bool(provenance.get('success')),
  'rawResponseFile':'site-osm.json' if provenance.get('success') else None,
  'shuiyuanMarket':{'bbox':None,'heightMetres':None,'buildingLevels':None,'status':'No raw OSM response acquired; no coordinates, footprint or height inferred.'},
  'tingzhouLane160AndWaterwayTrail':{'geometry':None,'status':'Names corroborated by Taipei UDD public financial record, but no raw road geometry acquired.',
   'primarySource':'https://udd.gov.taipei/assets/422-4468/Documents/97%E5%B9%B4%E5%BA%A6%E5%96%AE%E4%BD%8D%E6%B1%BA%E7%AE%97.pdf',
   'evidence':'Document mentions the joint Roosevelt Section 4 Lane 90 / Tingzhou Section 3 Lane 160 waterway-trail landscape project. This does not supply coordinates or widths.'},
  'jason158':{'address':'臺北市中正區汀州路三段158號','officialAddressSource':'https://page.line.me/373fqoko',
   'osmCandidateNodeId':4524328182,'candidateIdDiscovery':'https://mapcarta.com/N4524328182',
   'coordinate':None,'oppositeSideGeometryConfirmed':False,
   'limitation':'Official page confirms address only. OSM candidate node was found through a secondary OSM-derived listing but its original coordinates were not retrieved; no placement inferred.'},
  'requestLimit':'One main endpoint and one alternative public endpoint attempted; 504 and 500 respectively. Stopped instead of repeated retries. Initial sandbox DNS failures required the same bounded fetch to run with approved network access.'
 }
report['stagedLaneCorrection']={
 'editorSource':'UnitySiteMatch/Assets/TaiwanRider/Editor/SiteLaneSetup.cs',
 'integrationOrder':['IntersectionDetailSetup.Apply(-1,3.2f,19f,includeAlleyBackdrop:false)','SiteLaneSetup.Apply()'],
 'newRootName':'Lane 90 • OSM centreline visual correction',
 'preservedOriginal':'Street geometry/Lane 90 collider, transform, layer and hazard runtime',
 'changes':'Disable only original Lane 90 MeshRenderer; add visual road mesh along lanePoints with tangent-normal offsets; remove invented backdrop by the original generator option.',
 'widthMetres':9,'widthIsEstimated':True,'heightMetres':0.003,'heightIsSurveyed':False,
 'startX':5.5,'farEnd':survey['lanePoints'][-1],'newColliders':0,
 'remainingLimitation':'Distant surface is visual only; existing straight near collider intentionally retained for gameplay. This is not a surveyed roadway envelope. 160-lane connection and surrounding real buildings remain unresolved.'
}
(HERE/'geometry-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'junctionMatches':report['conclusion']['namedGeographicJunctionMatchesCachedOSM'],'buildingCount':len(layout),'heightDistribution':dict(heights),'widthDistribution':dict(widths),'laneLength':lane_length,'laneAtX32_5':lane_z(32.5),'laneAtX76':lane_z(76),'overlapBuildings':sample_buildings,'playableCentrelineLength':play_length},ensure_ascii=False,indent=2))
