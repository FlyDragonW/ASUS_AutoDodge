"""Original metric scooter, sedan, and Taipei shopfront meshes, generated in Blender.
Coordinates: X across street, Y forward along street, Z up. Authoring data retained in .blend.
"""
import bpy, math, random, json, pathlib, sys
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets'/'TaiwanRider'/'Art'/'Models'
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT/'Sources'/'Blender';SOURCE.mkdir(parents=True,exist_ok=True)
random.seed(125)

def reset():
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.materials):bpy.data.materials.remove(block)

def mat(name,color,metal=0,rough=.65):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    return m

def assign(o,name,m,parent=None):
    o.name=name;o.data.materials.append(m)
    if parent:o.parent=parent
    return o

def empty(name,location=(0,0,0),parent=None):
    o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o);o.location=location
    if parent:o.parent=parent
    return o

def bevel(o,width=.02,segments=2):
    mod=o.modifiers.new('Soft manufactured edges','BEVEL');mod.width=width;mod.segments=segments
    o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return o

def cube(name,loc,scale,m,b=.0,parent=None,uvunit=1):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.dimensions=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    uv=o.data.uv_layers.active
    if uv:
        for poly in o.data.polygons:
            n=poly.normal;axis=max(range(3),key=lambda k:abs(n[k]));inds=[k for k in range(3) if k!=axis]
            for li in poly.loop_indices:
                co=o.data.vertices[o.data.loops[li].vertex_index].co
                if m.name.startswith('Sign') or m.name in ['Instrument','Plate'] or name.startswith('MirrorSurface'):
                    u=co[inds[0]]/scale[inds[0]]+.5;v=co[inds[1]]/scale[inds[1]]+.5
                else:u=co[inds[0]]/uvunit;v=co[inds[1]]/uvunit
                if (axis==0 and n.x<0) or (axis==1 and n.y>0):u=-u
                uv.data[li].uv=(u,v)
    assign(o,name,m,parent)
    if b:bevel(o,b)
    return o

def sphere(name,loc,scale,m,parent=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,location=loc)
    o=bpy.context.object;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    assign(o,name,m,parent)
    for p in o.data.polygons:p.use_smooth=True
    return o

def rod(name,a,b,r,m,parent=None,vertices=12):
    a,b=Vector(a),Vector(b);delta=b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=delta.length,location=(a+b)*.5)
    o=bpy.context.object;o.rotation_mode='QUATERNION';o.rotation_quaternion=delta.to_track_quat('Z','Y')
    return assign(o,name,m,parent)

def torus(name,loc,major,minor,m,parent=None,rotation=(0,math.pi/2,0)):
    bpy.ops.mesh.primitive_torus_add(major_segments=64,minor_segments=14,location=loc,rotation=rotation,major_radius=major,minor_radius=minor)
    o=bpy.context.object;assign(o,name,m,parent)
    for p in o.data.polygons:p.use_smooth=True
    return o

def loft(name,sections,m,parent=None):
    # y, centre height, half width, half height
    verts=[];faces=[];n=32
    for y,z,w,h in sections:
        for j in range(n):
            a=j*2*math.pi/n;verts.append((math.cos(a)*w,y,z+math.sin(a)*h))
    for i in range(len(sections)-1):
        for j in range(n):faces.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
    faces.append(tuple(reversed(range(n))));faces.append(tuple((len(sections)-1)*n+j for j in range(n)))
    faces=[tuple(reversed(face)) for face in faces]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);assign(o,name,m,parent)
    for p in mesh.polygons:p.use_smooth=True
    return o

def export(name):
    bpy.ops.object.select_all(action='SELECT')
    # Apply modifiers so every export and saved source produces the same geometry.
    for o in list(bpy.context.selected_objects):
        if o.type=='MESH':
            bpy.context.view_layer.objects.active=o
            for modifier in list(o.modifiers):
                try:bpy.ops.object.modifier_apply(modifier=modifier.name)
                except:pass
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(name+'.blend')))
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=False,add_leaf_bones=False,path_mode='STRIP')
    print('EXPORTED',name,len(bpy.data.objects),flush=True)

def scooter():
    reset()
    paint=mat('Scooter_Pearl',(.44,.48,.45),.42,.27);dark=mat('Charcoal',(.033,.039,.04),.08,.56)
    rubber=mat('Rubber',(.025,.027,.028),0,.92);seat=mat('SeatLeather',(.055,.057,.053),0,.8)
    alloy=mat('Alloy',(.39,.42,.42),.8,.24);chrome=mat('Chrome',(.68,.73,.75),.94,.17)
    lens=mat('Headlamp',(.85,.88,.79),.2,.19);red=mat('TailLens',(.52,.025,.013),.2,.27)
    amber=mat('Indicator',(.91,.38,.015),.16,.25);instrument=mat('Instrument',(.04,.045,.04));plate=mat('Plate',(.9,.9,.84))
    root=empty('Scooter_Model')
    # Precisely spaced wheel centres, metric 1275 mm wheelbase.
    for name,y in [('FrontWheel',.6375),('RearWheel',-.6375)]:
        wheel=empty(name,(0,y,.217),root)
        torus('Road tyre',(0,0,0),.173,.044,rubber,wheel)
        torus('Rim lip left',(-.043,0,0),.132,.012,alloy,wheel)
        torus('Rim lip right',(.043,0,0),.132,.012,alloy,wheel)
        rod('Wheel hub',(-.065,0,0),(.065,0,0),.043,alloy,wheel,32)
        for j in range(5):
            a=j*math.tau/5
            for x in [-.036,.036]:rod('Cast alloy spoke',(x,0,0),(x,.125*math.sin(a),.125*math.cos(a)),.015,alloy,wheel)
        for j in range(40):
            a=j*math.tau/40
            o=cube('Tread groove',(.0,.215*math.sin(a),.215*math.cos(a)),(.074,.004,.007),dark,parent=wheel)
            o.rotation_euler.x=-a
        if name=='FrontWheel':
            rod('Brake rotor',(-.063,0,0),(-.057,0,0),.092,chrome,wheel,48)
            for j in range(16):
                a=j*math.tau/16;sphere('Rotor perforation',(-.067,.073*math.sin(a),.073*math.cos(a)),(.003,.005,.005),dark,wheel)
    # Rear fairing has individually shaped sections; black lower engine and floor pan.
    loft('Rear pearl fairing',[(-.87,.51,.14,.065),(-.73,.56,.225,.17),(-.44,.54,.268,.18),(-.14,.53,.247,.20),(.07,.45,.18,.09)],paint,root)
    loft('Underseat black seam',[(-.80,.694,.16,.03),(-.50,.695,.255,.035),(-.15,.673,.234,.03),(.02,.633,.15,.026)],dark,root)
    loft('Sculpted seat',[(-.77,.744,.135,.029),(-.63,.752,.223,.046),(-.30,.73,.241,.043),(-.02,.695,.202,.04),(.105,.66,.134,.017)],seat,root)
    cube('Foot platform',(0,.14,.264),(.43,.66,.065),dark,.025,root)
    for x in [-.16,-.10,.10,.16]:rod('Footboard traction rib',(x,-.1,.303),(x,.39,.303),.008,rubber,root)
    # Swept forward shield, narrower at ankle height, wider at the upper apron.
    loft('Front leg shield',[(.30,.41,.14,.135),(.41,.54,.19,.244),(.52,.65,.265,.303),(.63,.70,.246,.285),(.72,.74,.155,.187)],paint,root)
    loft('Inner legshield',[(.275,.44,.13,.12),(.355,.54,.185,.22),(.46,.65,.235,.25)],dark,root)
    for x in [-.24,.24]:rod('Apron trim',(x*.66,.38,.34),(x,.56,.83),.008,alloy,root)
    # Mudguard above the front tyre, based on a short curved shell.
    loft('Front mudguard',[(.41,.42,.056,.022),(.53,.45,.082,.044),(.69,.45,.088,.043),(.81,.405,.068,.028)],paint,root)
    for x in [-.073,.073]:
        rod('Fork lower',(x,.6375,.23),(x,.53,.56),.025,alloy,root)
        rod('Fork stanchion',(x,.53,.56),(x,.43,.82),.014,chrome,root)
    steering=empty('FrontSteering',(0,.44,.93),root)
    loft('Handlebar front cowl',[(-.095,0,.20,.054),(.005,.027,.265,.072),(.095,.01,.211,.058)],paint,steering)
    cube('Instrument surround',(0,-.067,.055),(.292,.185,.052),dark,.029,steering)
    gauge=cube('Instrument lens',(0,-.071,.085),(.252,.155,.007),instrument,.007,steering)
    gauge.rotation_euler.x=math.radians(-17)
    for side in [-1,1]:
        rod('Grip metal core',(side*.21,-.014,.02),(side*.3425,-.055,.015),.014,alloy,steering)
        rod('Rubber grip',(side*.248,-.025,.022),(side*.338,-.052,.022),.021,rubber,steering,24)
        for j in range(8):
            torus('Grip grooves',(side*(.249+j*.011),-.025-j*.003,.022),.021,.0018,dark,steering,rotation=(0,math.pi/2,0))
        cube('Switch housing',(side*.225,-.018,.024),(.047,.05,.042),dark,.009,steering)
        cube('Red switch',(side*.222,-.041,.041),(.018,.015,.012),red,.003,steering)
        rod('Brake lever',(side*.215,.032,.025),(side*.323,.008,.021),.007,chrome,steering)
        rod('Mirror stalk',(side*.206,.018,.038),(side*.283,.018,.176),.006,chrome,steering)
        rod('Mirror angled stalk',(side*.283,.018,.176),(side*.33,.024,.188),.006,chrome,steering)
        sphere('Mirror shell',(side*.327,.048,.188),(.067,.024,.042),dark,steering)
        mirror=cube('MirrorSurface_'+('L' if side<0 else 'R'),(side*.327,.021,.188),(.108,.005,.063),chrome,.014,steering)
    sphere('Clear headlight',(0,.692,.966),(.148,.056,.069),lens,root)
    for x in [-.152,.152]:sphere('Front amber indicator',(x,.697,.855),(.037,.031,.047),amber,root)
    sphere('Rear tail lamp',(0,-.845,.618),(.15,.016,.045),red,root)
    for x in [-.184,.184]:sphere('Rear indicator',(x,-.825,.60),(.035,.022,.027),amber,root)
    cube('Rear registration plate',(0,-.879,.422),(.225,.01,.105),plate,.005,root)
    rod('Rear grabrail left',(-.2,-.52,.765),(-.18,-.79,.78),.014,alloy,root)
    rod('Rear grabrail right',(.2,-.52,.765),(.18,-.79,.78),.014,alloy,root)
    rod('Rear grabrail bridge',(-.18,-.79,.78),(.18,-.79,.78),.014,alloy,root)
    sphere('Engine case',(-.114,-.50,.26),(.085,.285,.105),dark,root)
    rod('Muffler',( .19,-.22,.26),(.19,-.72,.22),.066,dark,root,32)
    rod('Muffler shield',(.246,-.29,.28),(.246,-.68,.25),.029,alloy,root,24)
    for side in [-1,1]:
        rod('Rear shock',(side*.15,-.57,.26),(side*.16,-.47,.65),.014,alloy,root)
        for j in range(10):torus('Shock spring',(side*.16,-.48-j*.006,.62-j*.023),.023,.004,dark,root,rotation=(0,0,0))
    # Original neutral riding gloves and jacket sleeves, exported independently so NPCs omit them.
    export('Scooter125')
    for o in list(bpy.data.objects):
        if o.type=='MESH' and o.name.startswith(('Tread','Rotor','Grip grooves','Shock spring')):bpy.data.objects.remove(o,do_unlink=True)
    for o in list(bpy.data.objects):
        if o.type=='MESH' and len(o.data.polygons)>80:
            m=o.modifiers.new('Parked vehicle LOD','DECIMATE');m.ratio=.35
    export('Scooter125_LOD')

def sedan():
    reset()
    paint=mat('Sedan_Pearl',(.65,.66,.62),.52,.22);black=mat('Charcoal',(.035,.041,.043),.05,.6)
    glass=mat('Glass',(.09,.16,.18),.48,.17);rubber=mat('Rubber',(.028,.030,.029),0,.88)
    chrome=mat('Chrome',(.63,.69,.7),.94,.17);lamp=mat('Headlamp',(.85,.86,.75),.3,.18);red=mat('TailLens',(.5,.02,.015),.2,.3)
    root=empty('Sedan_Model')
    loft('Lower body',[(-2.23,.47,.64,.16),(-2.12,.49,.85,.23),(-1.5,.50,.89,.25),(1.4,.51,.87,.26),(2.12,.47,.81,.20),(2.27,.48,.67,.10)],paint,root)
    loft('Bonnet and trunk',[(-2.14,.77,.69,.08),(-1.36,.82,.80,.10),(.81,.86,.80,.12),(1.72,.79,.76,.08),(2.17,.66,.63,.06)],paint,root)
    loft('Passenger cabin',[(-1.48,.99,.72,.12),(-.77,1.14,.705,.27),(.55,1.17,.69,.29),(1.22,.99,.745,.105)],glass,root)
    loft('Roof',[(-.87,1.395,.65,.015),(-.6,1.443,.661,.018),(.4,1.453,.647,.016),(.61,1.40,.636,.012)],paint,root)
    for side in [-1,1]:
        for y in [-.88,.52]:rod('Window pillar',(side*.735,y+(.28 if y<0 else .6),.9),(side*.655,y,1.406),.039,paint,root)
        rod('B pillar',(side*.715,-.24,.9),(side*.654,-.24,1.434),.033,black,root)
        rod('Window chrome trim',(side*.782,-1.35,.896),(side*.77,1.12,.94),.01,chrome,root)
        for y in [-.72,.48]:
            rod('Door shut line',(side*.878,y,.47),(side*.84,y,.94),.004,black,root)
            cube('Door handle',(side*.858,y-.2,.877),(.026,.14,.026),chrome,.008,root)
        sphere('Door mirror',(side*.954,.73,1.014),(.108,.16,.062),paint,root)
        for y in [-1.36,1.30]:
            wheel=empty('SedanWheel',(side*.802,y,.31),root)
            torus('Tyre',(0,0,0),.251,.062,rubber,wheel)
            torus('Alloy rim',(side*.048,0,0),.207,.014,chrome,wheel)
            rod('Hub',(0,0,0),(side*.052,0,0),.054,chrome,wheel)
            for j in range(10):
                a=j*math.tau/10;rod('Spoke',(side*.058,0,0),(side*.061,.203*math.sin(a),.203*math.cos(a)),.013,chrome,wheel)
    cube('Front grille',(0,2.245,.48),(1.11,.018,.19),black,.035,root)
    for z in [.43,.49,.55]:rod('Chrome grille',(-.51,2.26,z),(.51,2.26,z),.012,chrome,root)
    for x in [-.62,.62]:
        sphere('Headlight',(x,2.10,.68),(.193,.065,.07),lamp,root)
        sphere('Tail light',(x,-2.12,.72),(.184,.024,.066),red,root)
    plate=mat('Plate',(.9,.9,.84));cube('Front plate',(0,2.278,.46),(.39,.012,.19),plate,.006,root)
    cube('Rear plate',(0,-2.25,.57),(.39,.01,.19),plate,.006,root)
    export('Sedan')

def environment():
    reset();random.seed(125)
    concrete=mat('Concrete',(.71,.70,.65));tile=mat('FacadeTile',(.67,.65,.56));dark=mat('Charcoal',(.07,.085,.08),.1,.7)
    aluminum=mat('Alloy',(.40,.44,.43),.72,.3);glass=mat('Glass',(.16,.23,.23),.28,.24)
    cream=mat('OldCream',(.65,.62,.50));green=mat('OldGreen',(.46,.52,.43));pink=mat('OldRose',(.61,.48,.42))
    white=mat('AirconWhite',(.73,.73,.65));rust=mat('RustMetal',(.31,.17,.12),.35,.83)
    shutter=mat('Shutter',(.39,.43,.40),.55,.58);awning=mat('Awning',(.26,.36,.30),0,.75)
    water=mat('TankSteel',(.5,.55,.52),.8,.33);plant=mat('Leaves',(.17,.27,.10));pot=mat('Terracotta',(.40,.20,.10))
    signmats={}
    for j in range(16):
        for orientation in ['H','V']:signmats[j,orientation]=mat(f'Sign_{j:02d}_{orientation}',(1,1,1))
    layout=[]
    def facade(side,center,width,index):
        floors=random.choice([3,4,4,5]);height=3.3+floors*2.9;root=empty(f'Building_{side}_{index}')
        shade=[concrete,tile,cream,green,pink][index%5]
        cube('Upper tiled volume',(side*12,center,(height+3.2)/2),(8.7,width-.1,height-3.2),shade,.025,root,1.6)
        cube('Shop rear wall',(side*11,center,1.65),(.3,width-.1,3.3),concrete,0,root,1.6)
        cube('Arcade ceiling',(side*9.2,center,3.12),(3.3,width,.23),concrete,.015,root)
        for y in [center-width/2+.22,center+width/2-.22]:
            cube('Arcade tiled pillar',(side*7.76,y,1.62),(.46,.46,3.24),shade,.012,root,.5)
            cube('Column plinth',(side*7.76,y,.27),(.54,.54,.42),concrete,.01,root)
        # Ground-floor glazing, inset shop entry, or a shutter with horizontal slats.
        if index%4==0:
            cube('Closed rolling shutter',(side*10.7,center,1.48),(.055,width-.65,2.65),shutter,.012,root)
            for k in range(23):cube('Shutter corrugation',(side*10.655,center,.26+k*.107),(.012,width-.68,.018),aluminum,0,root)
        else:
            cube('Shop glass',(side*10.67,center,1.48),(.025,width-.7,2.6),glass,0,root)
            for y in [center-width*.35,center,center+width*.35]:cube('Shop mullion',(side*10.60,y,1.48),(.065,.047,2.62),aluminum,0,root)
            for z in [.23,2.73]:cube('Shop frame',(side*10.60,center,z),(.065,width-.68,.05),aluminum,0,root)
            cube('Door handle',(side*10.54,center+.12,1.26),(.03,.033,.35),aluminum,.009,root)
            for k in range(3):cube('Interior shelf',(side*11.2,center-width*.3+k*width*.3,1.01),(.37,.73,1.5),cream,.025,root)
        cube('Shop fascia',(side*7.57,center,3.48),(.19,width-.17,.76),signmats[index%16,'H'],.025,root)
        if index%2==0:
            cube('Projecting signbox',(side*6.98,center+width*.36,5.16),(1.2,.19,2.76),signmats[index%16,'V'],.034,root)
            rod('Sign bracket',(side*7.5,center+width*.36,6.22),(side*6.55,center+width*.36,6.22),.027,rust,root)
        for floor in range(floors):
            z=4.9+floor*2.9
            count=max(2,int(width/2.4))
            for j in range(count):
                y=center-width*.36+j*(width*.72/(count-1));w=min(1.72,width/count-.25)
                cube('Window shadow',(side*7.623,y,z),(.09,w+.15,1.66),dark,0,root)
                cube('Window glazing',(side*7.559,y,z),(.019,w,1.49),glass,0,root)
                for dy in [-w/2,0,w/2]:cube('Aluminum window frame',(side*7.535,y+dy,z),(.052,.036,1.55),aluminum,0,root)
                for dz in [-.765,.0,.765]:cube('Aluminum window sill',(side*7.535,y,z+dz),(.063,w+.04,.034),aluminum,0,root)
                if (j+floor+index)%3==0:
                    for k in range(7):rod('Window security grille',(side*7.34,y-w*.45+k*w*.15,z-.7),(side*7.34,y-w*.45+k*w*.15,z+.7),.009,aluminum,root)
                    for dz in [-.7,.7]:rod('Grille horizontal',(side*7.34,y-w*.49,z+dz),(side*7.34,y+w*.49,z+dz),.016,aluminum,root)
                if (j+floor+index)%2==0:
                    cube('Air conditioner',(side*7.35,y+.40,z-1.14),(.42,.75,.46),white,.02,root)
                    for k in range(6):cube('Aircon louvre',(side*7.123,y+.40,z-1.3+k*.051),(.018,.60,.012),aluminum,0,root)
                    rod('Aircon drain',(side*7.3,y+.78,z-1.08),(side*7.3,y+.78,z-2.0),.009,cream,root)
            cube('Floor lip',(side*7.56,center,z+1.12),(.19,width,.09),concrete,0,root)
        rod('Exterior rainwater pipe',(side*7.40,center-width*.47,.26),(side*7.40,center-width*.47,height+.1),.032,rust,root)
        cube('Parapet',(side*7.87,center,height+.22),(.30,width,.52),concrete,.01,root)
        if index%3==0:
            rod('Stainless rooftop water tank',(side*10,center,height+.3),(side*10,center,height+1.65),.53,water,root,32)
            for z in [height+.4,height+.8,height+1.2,height+1.6]:torus('Tank rib',(side*10,center,z),.53,.019,aluminum,root,rotation=(0,0,0))
        if index%3!=1:
            o=cube('Canvas sunshade',(side*7.2,center,2.80),(1.5,width-.6,.09),awning,.02,root);o.rotation_euler.y=side*.13
            for dy in [-width*.38,width*.38]:rod('Awning support',(side*7.95,center+dy,2.30),(side*6.57,center+dy,2.73),.019,aluminum,root)
        if index%2==1:
            for k in range(2):
                y=center-width*.37+k*.5
                rod('Plant pot',(side*8.36,y,.2),(side*8.36,y,.65),.20,pot,root,16)
                for j in range(5):
                    a=j*math.tau/5
                    sphere('Broadleaf plant',(side*8.36+.22*math.sin(a),y+.22*math.cos(a),.81+random.random()*.2),(.19,.13,.31),plant,root)
        layout.append({'name':root.name,'side':side,'center':center,'width':width,'height':height})
        # Join each material per building to keep hierarchy and draw count manageable.
        objects=[o for o in root.children if o.type=='MESH']
        groups={}
        for o in objects:groups.setdefault(o.data.materials[0].name,[]).append(o)
        for material_name,group in groups.items():
            bpy.ops.object.select_all(action='DESELECT')
            for o in group:
                o.select_set(True);bpy.context.view_layer.objects.active=o
                for mod in list(o.modifiers):
                    try:bpy.ops.object.modifier_apply(modifier=mod.name)
                    except:pass
            bpy.context.view_layer.objects.active=group[0]
            if len(group)>1:bpy.ops.object.join()
            group[0].name=f'{root.name}_{material_name}'
    index=0
    for side in [-1,1]:
        y=-108
        while y<108:
            width=random.choice([6.4,7.2,8.0,9.4])
            if side==1 and y<6 and y+width>-6:
                y=6;continue
            facade(side,y+width/2,width,index);index+=1;y+=width
    (ROOT/'Sources'/'building-layout.json').write_text(json.dumps(layout,indent=2),encoding='utf-8')
    export('GongguanBuildings')

scooter();sedan()
if '--vehicles-only' not in sys.argv:environment()
print('ALL MODELS READY',flush=True)
