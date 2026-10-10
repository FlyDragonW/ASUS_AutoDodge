import json,math,pathlib
root=pathlib.Path(__file__).resolve().parents[1]
data=json.loads((root/'Sources'/'gongguan-osm.json').read_text(encoding='utf-8'))
origin=(25.0135214,121.5344569)
# Drive towards the northwest. East/north are rotated into right/forward local metric axes.
forward=(-.77865,.62747);right=(.62747,.77865)
def local(p):
    e=(p['lon']-origin[1])*111320*math.cos(math.radians(origin[0]));n=(p['lat']-origin[0])*111320
    return {'x':round(e*right[0]+n*right[1],4),'y':0,'z':round(e*forward[0]+n*forward[1],4)}
main=next(x for x in data['elements'] if x['tags']['name']=='汀州路三段')
lane=next(x for x in data['elements'] if x['tags']['name']=='羅斯福路四段90巷')
points=sorted([local(p) for p in main['geometry']],key=lambda p:p['z'])
route={'originLatitude':origin[0],'originLongitude':origin[1],'sourceWayId':main['id'],'referenceDate':data['osm3s']['timestamp_osm_base'],'points':points,'lanePoints':[local(p) for p in lane['geometry']],'roadWidthMetres':12.4,'widthIsEstimated':True}
out=root/'Assets'/'TaiwanRider'/'Data';out.mkdir(parents=True,exist_ok=True)
(out/'RouteSurvey.json').write_text(json.dumps(route,indent=2),encoding='utf-8')
print(json.dumps(route,indent=2))
