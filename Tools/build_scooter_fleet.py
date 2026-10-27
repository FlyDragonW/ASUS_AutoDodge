"""Build reusable static traffic/parking LODs from the original Scooter125_Hero.

Blender 5.1: blender --background --factory-startup --threads 4 --python this.py
Optionally append -- --preview for one compact, <=12-sample studio check.
The animated player keeps its separate Hero mesh. Fleet wheel transforms carry
one mesh each for displacement-driven visual spin; steering remains a landmark.
"""
import bpy, bmesh, json, pathlib, math, sys, hashlib
from mathutils import Vector

ROOT=pathlib.Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'UnityRealismUpdate/Sources/Blender/Scooter125_Hero.blend'
OUT=ROOT/'Assets/TaiwanRider/Art/Models'
BLENDS=ROOT/'Sources/Blender'
CHECK=ROOT/'Validation/ScooterFleet'
for path in (OUT,BLENDS,CHECK):path.mkdir(parents=True,exist_ok=True)

GROUPS={
 'ShellAndSeat':['Hero_Pearl','HeroSeatLeather'],
 'DarkTrimAndTyres':['Charcoal','Rubber'],
 'CastAndBrushedMetal':['Alloy','HeroBrushedMetal'],
 'ChromeAndMirrors':['Chrome'],
 'LampOptics':['Headlamp','Indicator','TailLens'],
 'InstrumentAndPlate':['Instrument','Plate','HeroGaugeNeedle'],
 'SeatStitch':['HeroSeatStitch'],
}
# Preserve the curved body first; aggressively simplify subpixel tread/coil detail.
RATIOS=[
 {'Hero_Pearl':.95,'HeroSeatLeather':.72,'Charcoal':.50,'Rubber':.68,'Alloy':.55,'Chrome':.55,'HeroBrushedMetal':.50,'Headlamp':.66,'TailLens':.70,'Indicator':.65,'HeroSeatStitch':.30},
 {'Hero_Pearl':.50,'HeroSeatLeather':.30,'Charcoal':.105,'Rubber':.36,'Alloy':.20,'Chrome':.17,'HeroBrushedMetal':.16,'Headlamp':.22,'TailLens':.24,'Indicator':.24,'HeroSeatStitch':.09},
 {'Hero_Pearl':.16,'HeroSeatLeather':.12,'Charcoal':.021,'Rubber':.14,'Alloy':.06,'Chrome':.06,'HeroBrushedMetal':.04,'Headlamp':.09,'TailLens':.095,'Indicator':.07,'HeroSeatStitch':.035},
]
RANGES=[(35000,55000),(10000,18000),(3000,6000)]
NODES=['FrontWheel','RearWheel','FrontSteering','MirrorSurface_L','MirrorSurface_R','Instrument lens','GaugeNeedle']
LOCKED={'MirrorSurface_L','MirrorSurface_R','Instrument lens','GaugeNeedle'}

def tri_count(o):return sum(len(p.vertices)-2 for p in o.data.polygons)

def remove_small_islands(mesh,threshold):
 """Drop subpixel standalone bolts/sipes/stitches, instead of mangling their tubes."""
 bm=bmesh.new();bm.from_mesh(mesh);todo=set(bm.verts);remove=[];count=0
 while todo:
  seed=todo.pop();island={seed};stack=[seed]
  while stack:
   vertex=stack.pop()
   for edge in vertex.link_edges:
    other=edge.other_vert(vertex)
    if other in todo:todo.remove(other);island.add(other);stack.append(other)
  diameter=max(max(v.co[i] for v in island)-min(v.co[i] for v in island) for i in range(3))
  if diameter<threshold:remove.extend(island);count+=1
 if remove:bmesh.ops.delete(bm,geom=remove,context='VERTS')
 bm.to_mesh(mesh);bm.free();return count

def bounds(objects):
 points=[o.matrix_world@Vector(corner) for o in objects for corner in o.bound_box]
 lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
 return {'min':lo,'max':hi,'dimensions':[hi[i]-lo[i] for i in range(3)]}

def data_uv_bounds(objects,material):
 values=[]
 for o in objects:
  if not o.data.uv_layers:continue
  uv=o.data.uv_layers.active or o.data.uv_layers[0]
  for p in o.data.polygons:
   if o.data.materials[p.material_index].name==material:
    values.extend(uv.data[i].uv[:] for i in p.loop_indices)
 return [[min(v[i] for v in values),max(v[i] for v in values)] for i in range(2)] if values else None

report={'source':str(SOURCE),'sourceSHA256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
 'authoringAxes':'X right / Y forward / Z up, metres',
 'purpose':'Instanced parked scooters and moving HazardActor with independent visual wheel spin; not the player',
 'allMaterialsReusedFromHero':True,'independentWheelMeshes':True,'lods':[]}

for level,ratios in enumerate(RATIOS):
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
 bpy.context.preferences.filepaths.save_version=0
 bpy.context.scene.render.threads_mode='FIXED';bpy.context.scene.render.threads=4
 bpy.context.view_layer.update()
 node_defs={name:{'matrix':bpy.data.objects[name].matrix_local.copy(),'parent':bpy.data.objects[name].parent.name} for name in NODES}
 root=bpy.data.objects['Scooter_Model']
 material_info=[{'name':m.name,'color':list(m.diffuse_color),
  'metallic':m.node_tree.nodes['Principled BSDF'].inputs['Metallic'].default_value,
  'roughness':m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value} for m in bpy.data.materials]
 meshes=[o for o in bpy.data.objects if o.type=='MESH']
 source_bounds=bounds(meshes)
 source_tri=sum(tri_count(o) for o in meshes)
 reduction=[]
 for o in meshes:
  old_name=o.name;old_tri=tri_count(o);material=o.data.materials[0].name
  limits=[[min(v.co[i] for v in o.data.vertices),max(v.co[i] for v in o.data.vertices)] for i in range(3)]
  removed=0
  if level==2 and old_name not in LOCKED and material in ['Charcoal','Alloy','Chrome','HeroBrushedMetal','HeroSeatStitch']:
   removed=remove_small_islands(o.data,.060)
  # Mirror UVs, calibration anchors and flat texture cards are inexpensive and kept exact.
  ratio=1 if old_name in LOCKED or material in ['Instrument','Plate','HeroGaugeNeedle'] else ratios.get(material,1)
  if ratio<.999 and len(o.data.polygons)>3:
   bpy.context.view_layer.objects.active=o
   modifier=o.modifiers.new('Fleet silhouette-priority reduction','DECIMATE')
   modifier.decimate_type='COLLAPSE';modifier.ratio=ratio;modifier.use_collapse_triangulate=True
   bpy.ops.object.modifier_apply(modifier=modifier.name)
  # Collapse quadrics may extrapolate thin rods beyond the original silhouette.
  # Keep all results inside the exact source-part bounds; pivots stay untouched.
  clamped=0
  for vertex in o.data.vertices:
   before=vertex.co.copy()
   for i,(lo,hi) in enumerate(limits):vertex.co[i]=max(lo,min(hi,vertex.co[i]))
   clamped+=int((before-vertex.co).length>.00001)
  # Bake original parent transform into vertices before cross-part material merging.
  world=o.matrix_world.copy();o.parent=None;o.matrix_world=world
  bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
  bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
  o.name='FleetSource__'+old_name
  reduction.append({'part':old_name,'material':material,'before':old_tri,'after':tri_count(o),'ratio':ratio,'removedSubpixelIslands':removed,'boundedCollapsedVertices':clamped})
 # Keep exact anchors; wheel visual meshes will be rebased into their original pivots.
 for o in list(bpy.data.objects):
  if o.type=='EMPTY' and o!=root:bpy.data.objects.remove(o,do_unlink=True)
 for name,definition in node_defs.items():
  node=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(node)
  node.parent=root if definition['parent']=='Scooter_Model' else bpy.data.objects[definition['parent']]
  node.matrix_local=definition['matrix']
 bpy.context.view_layer.update()
 merged=[]
 wheel_parts={name:[o for o in meshes if o.name.startswith('FleetSource__'+name+'__')] for name in ['FrontWheel','RearWheel']}
 body_parts=[o for o in meshes if o not in wheel_parts['FrontWheel'] and o not in wheel_parts['RearWheel']]
 part_groups=[(group_name,[o for o in body_parts if o.data.materials[0].name in names],'Scooter_Model') for group_name,names in GROUPS.items()]
 part_groups.extend((name+'Assembly',parts,name) for name,parts in wheel_parts.items())
 for group_name,parts,parent_name in part_groups:
  if not parts:continue
  bpy.ops.object.select_all(action='DESELECT')
  for o in parts:o.select_set(True)
  bpy.context.view_layer.objects.active=parts[0]
  if len(parts)>1:bpy.ops.object.join()
  o=bpy.context.object;o.name='Fleet_'+group_name+'_LOD'+str(level)
  parent=bpy.data.objects[parent_name]
  o.data.transform(parent.matrix_world.inverted());o.parent=parent;o.matrix_world=parent.matrix_world.copy()
  if len(o.data.uv_layers):o.data.uv_layers.active_index=0
  # Remove any redundant unused slots created by join. All 14 original material names stay.
  used={p.material_index for p in o.data.polygons}
  for i in reversed(range(len(o.data.materials))):
   if i not in used:o.data.materials.pop(index=i)
  # Export explicit, nondegenerate triangles so FBX import cannot choose a
  # different n-gon tessellation or discard collapsed zero-area faces silently.
  bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.triangulate(bm,faces=list(bm.faces))
  flat=[f for f in bm.faces if f.calc_area()<1e-12]
  if flat:bmesh.ops.delete(bm,geom=flat,context='FACES_ONLY')
  bm.to_mesh(o.data);bm.free()
  o.data.validate(verbose=False,clean_customdata=False)
  merged.append(o)
 bpy.context.view_layer.update()
 total=sum(tri_count(o) for o in merged)
 new_bounds=bounds(merged)
 anchors={n:list(bpy.data.objects[n].matrix_world.translation) for n in NODES}
 wheelbase=(bpy.data.objects['FrontWheel'].matrix_world.translation-bpy.data.objects['RearWheel'].matrix_world.translation).length
 uvs={mat:data_uv_bounds(merged,mat) for mat in ['Instrument','Plate']}
 used_materials=sorted({m.name for o in merged for m in o.data.materials})
 checks={
  'nodesPresent':all(n in bpy.data.objects for n in NODES),
  'wheelbase1275mm':abs(wheelbase-1.275)<.0001,
  'frontIsPositiveY':anchors['FrontWheel'][1]>anchors['RearWheel'][1],
  'finiteGeometry':all(all(math.isfinite(v) for v in vert.co) for o in merged for vert in o.data.vertices),
  'rendererBudget':5<=len(merged)<=12,
  'triangleBudget':RANGES[level][0]<=total<=RANGES[level][1],
  'sameMaterialNames':used_materials==sorted(m['name'] for m in material_info),
  'instrumentAndPlateUVsPreserved':all(uv and all(abs(a)<.0001 and abs(b-1)<.0001 for a,b in uv) for uv in uvs.values()),
  'sameGroundAndBounds':max(abs(new_bounds[k][i]-source_bounds[k][i]) for k in ['min','max'] for i in range(3))<.012,
 }
 # Body material groups plus one renderer / five submeshes per independently rotating wheel.
 submeshes=sum(len(o.data.materials) for o in merged)
 checks['twentyFourMaterialSubmeshes']=submeshes==24
 checks['independentWheelMeshes']=all(len([o for o in merged if o.parent==bpy.data.objects[n]])==1 for n in ['FrontWheel','RearWheel'])
 lod={'level':level,'asset':'Scooter125_Fleet_LOD'+str(level),'sourceTriangles':source_tri,'triangles':total,
  'meshRenderers':len(merged),'materialSubmeshes':submeshes,'allObjects':len(bpy.data.objects),
  'wheelbase':wheelbase,'bounds':new_bounds,'anchors':anchors,'textureUVBounds':uvs,
  'meshes':[{'name':o.name,'triangles':tri_count(o),'materials':[m.name for m in o.data.materials]} for o in merged],
  'reductions':reduction,'checks':checks}
 report['lods'].append(lod);report['materials']=material_info
 assert all(checks.values()),json.dumps(lod,ensure_ascii=False)
 bpy.ops.object.select_all(action='SELECT')
 bpy.ops.wm.save_as_mainfile(filepath=str(BLENDS/(lod['asset']+'.blend')))
 bpy.ops.export_scene.fbx(filepath=str(OUT/(lod['asset']+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=False,add_leaf_bones=False,path_mode='STRIP')
 print('FLEET_EXPORTED',json.dumps({k:lod[k] for k in ['level','triangles','meshRenderers','materialSubmeshes','checks']}),flush=True)

report['integration']=[
 'Build one LODGroup per parked scooter/hazard model, with all three roots at identical local position, rotation and scale.',
 'Correct source forward orientation once using FrontWheel versus RearWheel, exactly as for Hero.',
 'Suggested screenRelativeTransitionHeight: LOD0 .18, LOD1 .07, LOD2 .016, then cull; tune against actual street shots.',
 'Reuse the same URP shared material assets as Hero; enable instancing. No per-instance material cloning for ordinary fleet parts.',
 'Keep current separate player Hero; each Fleet FrontWheel/RearWheel contains one local-origin visual mesh for independent spin. FrontSteering remains a landmark.',
 'Mirror faces retain planar UVs in joined Chrome mesh and use a shared static reflective material; do not add MirrorView.',
 'Player, NPC collider and rigidbody remain on their existing wrappers. Generated FBX contains no collider or runtime component.',
 'Keep previous scooter assets for recovery; this script never writes the original Hero or Unity scene.',
]
(CHECK/'fleet-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')

# Independent FBX round trip confirms exported hierarchy, material names and dimensions.
roundtrip=[]
for lod in report['lods']:
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 for m in list(bpy.data.materials):bpy.data.materials.remove(m)
 bpy.ops.import_scene.fbx(filepath=str(OUT/(lod['asset']+'.fbx')))
 bpy.context.view_layer.update();objects=[o for o in bpy.data.objects if o.type=='MESH']
 actual_bounds=bounds(objects)
 fw=bpy.data.objects['FrontWheel'].matrix_world.translation;rw=bpy.data.objects['RearWheel'].matrix_world.translation
 checks={'nodesPresent':all(n in bpy.data.objects for n in NODES),'triangleCount':sum(tri_count(o) for o in objects)==lod['triangles'],
  'rendererCount':len(objects)==9,'materialCount':sum(len(o.data.materials) for o in objects)==24,
  'metricWheelbase':abs((fw-rw).length-1.275)<.0001,'frontOrientation':fw.y>rw.y,
  'matchingBounds':max(abs(actual_bounds[k][i]-lod['bounds'][k][i]) for k in ['min','max'] for i in range(3))<.0001,
  'textureUVs':all(all(abs(a)<.0001 and abs(b-1)<.0001 for a,b in data_uv_bounds(objects,m)) for m in ['Instrument','Plate'])}
 roundtrip.append({'asset':lod['asset'],'checks':checks});assert all(checks.values()),str(checks)+' expectedTriangles='+str(lod['triangles'])+' importedTriangles='+str(sum(tri_count(o) for o in objects))
(CHECK/'fbx-roundtrip.json').write_text(json.dumps(roundtrip,ensure_ascii=False,indent=2),encoding='utf-8')
print('FLEET_ALL_VERIFIED',flush=True)

if '--preview' in sys.argv:
 bpy.ops.wm.open_mainfile(filepath=str(BLENDS/'Scooter125_Fleet_LOD0.blend'))
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12;scene.cycles.use_denoising=True
 scene.render.threads_mode='FIXED';scene.render.threads=4
 scene.render.resolution_x=1050;scene.render.resolution_y=850;scene.render.resolution_percentage=100
 scene.world.color=(.18,.18,.18)
 for name in ['Instrument','Plate']:
  path=ROOT.parent/'UnityImplementation/Assets/TaiwanRider/Art/Signs'/(name+'.png')
  if not path.exists():path=pathlib.Path('C:/General/Unity_Project/ASUS_AutoDodge/Assets/TaiwanRider/Art/Signs')/(name+'.png')
  if path.exists():
   mat=bpy.data.materials[name];node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(path))
   mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
 bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.008));ground=bpy.context.object
 material=bpy.data.materials.new('PreviewGround');material.diffuse_color=(.12,.13,.14,1);ground.data.materials.append(material)
 for name,loc,power,size in [('Key',(2,1,4),550,3),('Fill',(-2,2,2),350,2),('Rim',(0,-3,3),550,2)]:
  data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
  obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);obj.location=loc
  obj.rotation_euler=(Vector((0,0,.5))-obj.location).to_track_quat('-Z','Y').to_euler()
 camera=bpy.data.cameras.new('PreviewCamera');camera.lens=60
 obj=bpy.data.objects.new('PreviewCamera',camera);bpy.context.collection.objects.link(obj)
 obj.location=(2.45,2.9,1.75);obj.rotation_euler=(Vector((0,0,.59))-obj.location).to_track_quat('-Z','Y').to_euler();scene.camera=obj
 scene.render.filepath=str(CHECK/'fleet-lod0-close.png');bpy.ops.render.render(write_still=True)
 print('FLEET_PREVIEW_READY',flush=True)
