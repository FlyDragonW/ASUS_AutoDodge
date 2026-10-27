"""Original contemporary compact sedan, metric geometry and three Unity-ready LODs.
X lateral, Y forward, Z up. No brand mesh, badge or photographic texture is used.
"""
import bpy, math, pathlib, json, sys
from mathutils import Vector

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets/TaiwanRider/Art/Models'; SOURCE=ROOT/'Sources/Blender'; CHECK=ROOT/'Validation/CarDetail'
for path in (OUT,SOURCE,CHECK): path.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for material in list(bpy.data.materials): bpy.data.materials.remove(material)
bpy.context.scene.unit_settings.system='METRIC'
bpy.context.scene.unit_settings.scale_length=1
root=bpy.data.objects.new('CompactSedan_Model',None);bpy.context.collection.objects.link(root)

def mat(name,color,metal,rough):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    return m
paint=mat('SedanBody',(.37,.43,.455),.58,.30)
trim=mat('SedanDarkTrim',(.015,.020,.023),.08,.47)
glass=mat('SedanGlass',(.040,.072,.085),.26,.16)
rubber=mat('SedanRubber',(.017,.020,.019),0,.88)
alloy=mat('SedanAlloy',(.52,.56,.57),.90,.23)
steel=mat('SedanBrakeSteel',(.23,.245,.24),.80,.47)
white=mat('SedanLED',(.86,.92,.94),.20,.19)
red=mat('SedanRedLens',(.41,.012,.007),.20,.22)
amber=mat('SedanAmberLens',(.80,.27,.015),.15,.26)
plate=mat('SedanPlate',(.82,.85,.82),0,.62)

def mesh(name,verts,faces,material,smooth=False,parent=root):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    o=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(o);o.parent=parent;o.data.materials.append(material)
    for p in me.polygons:p.use_smooth=smooth
    # A closed thin shell has an unambiguous outside. Open disconnected panels
    # must not rely on Recalculate Outside guessing the winding after batching;
    # Cycles' double-sided shading had hidden an inverted rear-window polygon.
    if name in ('Continuous roof panel','Laminated windscreen','Heated rear glass','Door glazing','Sharp headlamp dark surround','Rear lamp dark backing','Integrated swept grille recess'):
        shell=o.modifiers.new('Physical panel thickness','SOLIDIFY');shell.thickness=.012 if name=='Continuous roof panel' else .005;shell.offset=-1;shell.use_even_offset=True
    return o
def box(name,loc,scale,material,bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.parent=root;o.data.materials.append(material)
    if bevel:
        m=o.modifiers.new('Pressed edge radius','BEVEL');m.width=bevel;m.segments=3
        w=o.modifiers.new('Manufactured surface normals','WEIGHTED_NORMAL');w.keep_sharp=True
    return o
def tube(name,points,radius,material,sides=6):
    verts=[];faces=[]
    for i,p in enumerate(points):
        p=Vector(p);direction=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        tangent=direction.normalized();a=tangent.cross(Vector((0,0,1)))
        if a.length<.01:a=tangent.cross(Vector((0,1,0)))
        a.normalize();b=tangent.cross(a).normalized()
        for j in range(sides):verts.append(tuple(p+radius*(a*math.cos(j*math.tau/sides)+b*math.sin(j*math.tau/sides))))
    for i in range(len(points)-1):
        for j in range(sides):a=i*sides+j;b=i*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
    faces.extend([tuple(reversed(range(sides))),tuple((len(points)-1)*sides+j for j in range(sides))])
    return mesh(name,verts,faces,material,True)
def ellipsoid(name,loc,scale,material):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=loc);o=bpy.context.object;o.name=name;o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.parent=root;o.data.materials.append(material)
    for p in o.data.polygons:p.use_smooth=True
    return o
def anchor(name,loc):
    o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o);o.parent=root;o.location=loc;return o
def interp(y,knots):
    for i in range(1,len(knots)):
        a,b=knots[i-1],knots[i]
        if y<=b[0]:
            t=max(0,min(1,(y-a[0])/(b[0]-a[0])));span=b[0]-a[0]
            before=knots[max(0,i-2)];after=knots[min(len(knots)-1,i+1)]
            da=(b[1]-before[1])/(b[0]-before[0]);db=(after[1]-a[1])/(after[0]-a[0])
            return (2*t**3-3*t*t+1)*a[1]+(t**3-2*t*t+t)*span*da+(-2*t**3+3*t*t)*b[1]+(t**3-t*t)*span*db
    return knots[-1][1]
def width(y):return interp(y,[(-2.25,.735),(-2.10,.855),(-1.60,.876),(-.50,.880),(.85,.883),(1.63,.865),(2.10,.830),(2.25,.745)])
def belt(y):return interp(y,[(-2.25,.790),(-2.03,.885),(-1.38,.925),(-.35,.930),(.72,.935),(1.20,.899),(1.86,.836),(2.25,.783)])
def crown(y):return interp(y,[(-2.25,.020),(-2.02,.032),(-1.30,.028),(.78,.045),(1.72,.042),(2.25,.016)])
def body_y(y,u):
    front=max(0,(y-1.86)/.39);rear=max(0,(-y-1.90)/.35)
    return y-.15*u*u*front+.13*u*u*rear
def bottom(y):
    z=.225
    for centre in (-1.36,1.30):
        d=abs(y-centre)
        if d<.367:z=max(z,.318+math.sqrt(.367*.367-d*d))
    return z
def side_surface_x(y,z):
    t=max(0,min(1,(z-bottom(y))/max(.001,belt(y)-bottom(y))))
    return width(y)*(.959+.041*math.sin(t*math.pi*.52))

# A pressed, slab-sided body with distinct hood/trunk shoulders and actual wheel
# openings. Its section is not an oval/capsule, and the wheel wells remain open.
ny,nx=168,32
verts=[];faces=[]
for iy in range(ny+1):
    y=-2.25+4.5*iy/ny
    for ix in range(nx+1):
        u=-1+2*ix/nx;verts.append((width(y)*u,body_y(y,u),belt(y)+crown(y)*(1-u*u)))
for iy in range(ny):
    for ix in range(nx):a=iy*(nx+1)+ix;faces.append((a,a+1,a+nx+2,a+nx+1))
mesh('Continuous bonnet deck and shoulder skin',verts,faces,paint,True)
for side in (-1,1):
    verts=[];faces=[];nz=10
    for iy in range(ny+1):
        y=-2.25+4.5*iy/ny;low=bottom(y)
        for iz in range(nz+1):
            t=iz/nz;z=low+(belt(y)-low)*t
            # Lower sill tuck and a subtle upper shoulder crease.
            x=width(y)*(.959+.041*math.sin(t*math.pi*.52))
            verts.append((side*x,body_y(y,1),z))
    for iy in range(ny):
        for iz in range(nz):
            a=iy*(nz+1)+iz;quad=(a,a+nz+1,a+nz+2,a+1);faces.append(quad if side==1 else tuple(reversed(quad)))
    mesh('Sculpted side with open wheel arches',verts,faces,paint,True)
for y,sign in ((2.25,1),(-2.25,-1)):
    verts=[];faces=[]
    for iz in range(9):
        t=iz/8
        for ix in range(33):
            u=-1+2*ix/32;x=width(y)*u*(.94+.06*t)
            yy=body_y(y,u)+sign*.009*math.sin(t*math.pi)*(1-u*u)
            verts.append((x,yy,.245+.027*u*u+(belt(y)+crown(y)*(1-u*u)-.245-.027*u*u)*t))
    for iz in range(8):
        for ix in range(32):a=iz*33+ix;quad=(a,a+1,a+34,a+33);faces.append(quad if sign<0 else tuple(reversed(quad)))
    mesh('Front bumper skin' if sign>0 else 'Rear bumper skin',verts,faces,paint,True)
box('Underbody tray',(0,-.04,.195),(1.48,3.72,.060),trim,.025)
for side in (-1,1):
    box('Rocker sill',(side*.838,-.025,.243),(.055,1.86,.07),paint,.012)
    for centre in (-1.36,1.30):
        arc=[]
        for j in range(57):
            a=j*math.pi/56;yy=centre+.367*math.cos(a);zz=.318+.367*math.sin(a)
            arc.append((side*(width(yy)+.003),yy,zz))
        tube('Wheel opening rolled lip',arc,.010,paint,8)
        tube('Wheel well shadow liner',[(p[0]-side*.024,p[1],p[2]-.014) for p in arc],.018,trim,6)

# Roof and individually framed glass areas. Pillars have realistic section depth.
verts=[];faces=[]
for iy in range(49):
    y=-.88+1.23*iy/48;roofz=1.409+.028*math.sin(math.pi*iy/48);half=.639+.018*math.sin(math.pi*iy/48)
    for ix in range(25):
        u=-1+2*ix/24;verts.append((u*half,y,roofz+.022*(1-u*u)))
for iy in range(48):
    for ix in range(24):a=iy*25+ix;faces.append((a,a+1,a+26,a+25))
mesh('Continuous roof panel',verts,faces,paint,True)
for front in (True,False):
    verts=[];faces=[]
    for it in range(17):
        t=it/16
        y=(1.03+(.35-1.03)*t) if front else (-1.49+(-.88+1.49)*t)
        z=(.965+(1.409-.965)*t) if front else (.954+(1.409-.954)*t)
        half=.754+(.639-.754)*t
        for ix in range(33):
            u=-1+2*ix/32
            verts.append((u*half,y+(1 if front else -1)*.035*(1-u*u)*math.sin(t*math.pi),z+.022*(1-u*u)))
    for it in range(16):
        for ix in range(32):a=it*33+ix;quad=(a,a+1,a+34,a+33);faces.append(quad if not front else tuple(reversed(quad)))
    mesh('Laminated windscreen' if front else 'Heated rear glass',verts,faces,glass,True)
    for side in (-1,1):
        tube('A pillar' if front else 'C pillar',[(side*.770,1.04 if front else -1.50,.956),(side*.718,.75 if front else -1.25,1.177),(side*.648,.35 if front else -.88,1.415)],.018 if front else .030,paint,10)
    y0=1.03 if front else -1.49
    tube('Glass lower gasket',[(-.754,y0,.964),(-.38,y0,.982),(0,y0,.987),(.38,y0,.982),(.754,y0,.964)],.009,trim,6)
def window_x(z):return .777-(z-.952)*.29
for side in (-1,1):
    # Quads bounded by sill, pillars and gently arched roof; no whole-cabin blob.
    windows=[[(.965,.962),(-.195,.950),(-.195,1.411),(.316,1.401)],
             [(-.244,.950),(-1.415,.959),(-.869,1.399),(-.244,1.411)]]
    for polygon in windows:
        points=[(side*window_x(z),y,z) for y,z in polygon]
        faces=[tuple(range(4)) if side<0 else tuple(reversed(range(4)))]
        mesh('Door glazing',points,faces,glass,True)
        tube('Window seal',points+[points[0]],.008,trim,6)
    tube('B pillar',[(side*.779,-.22,.943),(side*.647,-.22,1.423)],.023,trim,8)
    tube('Window belt satin trim',[(side*.786,-1.40,.940),(side*.793,-.20,.940),(side*.786,.958,.945)],.008,alloy,6)
    tube('Roof side rain gutter',[(side*.656,-.88,1.426),(side*.671,-.30,1.454),(side*.654,.35,1.428)],.008,paint,8)
    # Four closed door cut lines trace the stamped metal rather than floating rods.
    for yline in (-1.04,-.22,.88):
        yz=[(yline,.926),(yline,.78),(yline,.56),(yline-.045,.34)]
        if abs(yline+1.04)<.01:yz=yz[:2]+[(-1.015,.690)]
        path=[(side*(side_surface_x(y,z)+.005),y,z) for y,z in yz]
        tube('Door shutline',path,.0028,trim,5)
    tube('Lower door seam',[(side*.854,-.985,.310),(side*.853,-.2,.302),(side*.856,.82,.320)],.003,trim,5)
    for y in (-.82,.58):
        box('Recessed handle pocket',(side*(width(y)+.001),y,.860),(.016,.172,.043),trim,.012)
        box('Body-colour handle',(side*(width(y)+.012),y,.870),(.026,.148,.025),paint,.009)
    tube('Side character crease',[(side*(width(y)+.004),y,belt(y)-.092) for y in [-2.0,-1.6,-1.0,-.2,.5,1.0,1.7,2.04]],.0035,paint,5)
    # Mirror shell, rear reflective insert, mounting base and indicator repeater.
    tube('Mirror mount',[(side*.765,.80,1.023),(side*.873,.755,1.067)],.026,trim,8)
    ellipsoid('Aerodynamic door mirror',(side*.925,.728,1.077),(.102,.150,.059),paint)
    ellipsoid('Mirror lower casing',(side*.925,.727,1.055),(.102,.148,.027),trim)
    ellipsoid('Mirror reflective face',(side*.937,.601,1.083),(.080,.006,.040),glass)
    tube('Mirror turn repeater',[(side*.999,.66,1.074),(side*1.014,.73,1.074),(side*.997,.81,1.078)],.005,amber,6)

# Wheel-and-tyre assemblies: moulded shoulders, five twin spokes, rotors and bolts.
def revolve(name,centre,profile,material,n=72):
    verts=[];faces=[];x,y,z=centre
    for dx,r in profile:
        for j in range(n):a=j*math.tau/n;verts.append((x+dx,y+math.sin(a)*r,z+math.cos(a)*r))
    for k in range(len(profile)-1):
        for j in range(n):a=k*n+j;b=k*n+(j+1)%n;faces.append((a,b,b+n,a+n))
    return mesh(name,verts,faces,material,True)
for side in (-1,1):
    for axle,y in [('Front',1.30),('Rear',-1.36)]:
        existing=set(bpy.data.objects)
        centre=(side*.795,y,.318)
        wheel=anchor('Wheel_'+('F' if axle=='Front' else 'R')+('L' if side<0 else 'R'),centre)
        profile=[(-.103,.226),(-.107,.255),(-.103,.282),(-.093,.304),(-.078,.315),(-.053,.318),(-.027,.317),(0,.316),(.027,.317),(.053,.318),(.078,.315),(.093,.304),(.103,.282),(.107,.255),(.103,.226)]
        revolve('Moulded road tyre',centre,profile,rubber)
        for band in (-.039,.039):revolve('Tyre longitudinal groove',centre,[(band-.004,.317),(band,.313),(band+.004,.317)],trim,72)
        for j in range(44):
            a=j*math.tau/44
            for half in (-1,1):
                pts=[]
                for k in range(3):
                    dx=half*(.04+.023*k);aa=a+half*.036*k;r=.319-abs(dx)*.08
                    pts.append((centre[0]+dx,y+math.sin(aa)*r,.318+math.cos(aa)*r))
                tube('Shoulder tread sipe',pts,.0018,trim,4)
        outer=side*.101
        revolve('Machined rim lip',centre,[(outer-side*.012,.228),(outer,.234),(outer+side*.005,.228),(outer,.219)],alloy,72)
        revolve('Rim dark barrel',centre,[(-.08,.211),(.08,.211)],trim,48)
        revolve('Ventilated brake rotor',centre,[(side*.067,.150),(side*.077,.150),(side*.077,.035),(side*.067,.035)],steel,64)
        for j in range(5):
            for split in (-1,1):
                a=j*math.tau/5+split*.080;b=a+side*.105
                inner=(centre[0]+side*.105,y+math.sin(a)*.046,.318+math.cos(a)*.046)
                end=(centre[0]+side*.103,y+math.sin(b)*.219,.318+math.cos(b)*.219)
                tube('Twin forged spoke',[inner,((inner[0]+end[0])*.5,(inner[1]+end[1])*.5,(inner[2]+end[2])*.5),end],.011,alloy,6)
            bolt=(centre[0]+side*.115,y+math.sin(j*math.tau/5)*.036,.318+math.cos(j*math.tau/5)*.036)
            ellipsoid('Wheel lug',bolt,(.004,.007,.007),steel)
        ellipsoid('Unbranded hub cap',(centre[0]+side*.112,y,.318),(.010,.033,.033),alloy)
        box('Brake caliper',(centre[0]+side*.070,y+.115,.340),(.035,.065,.090),trim,.007)
        bpy.context.view_layer.update()
        for part in set(bpy.data.objects)-existing:
            if part.type=='MESH' and not part.name.startswith('Brake caliper'):
                matrix=part.matrix_world.copy();part.parent=wheel;part.matrix_world=matrix

# Angular contemporary lamps and a curved intake integrated with the bumper.
def front_y(x):return 2.25-.15*(x/.745)**2
verts=[];faces=[]
for iz in range(9):
    t=iz/8;z=.326+.239*t;half=.533+.068*math.sin(t*math.pi*.65)
    for ix in range(33):
        x=half*(-1+2*ix/32);verts.append((x,front_y(x)+.017,z))
for iz in range(8):
    for ix in range(32):a=iz*33+ix;faces.append((a+1,a,a+33,a+34))
mesh('Integrated swept grille recess',verts,faces,trim,True)
for z in (.352,.399,.446,.493,.540):
    tube('Grille horizontal fin',[(x,front_y(x)+.023,z) for x in [-.533,-.39,-.27,-.13,0,.13,.27,.39,.533]],.006,trim,5)
for x in [i*.068 for i in range(-8,9)]:
    tube('Grille vertical blade',[(x,front_y(x)+.024,.348),(x,front_y(x)+.024,.542)],.0035,steel,4)
for side in (-1,1):
    points=[(side*.418,2.215,.787),(side*.810,2.020,.829),(side*.807,2.055,.754),(side*.455,2.235,.740)]
    mesh('Sharp headlamp dark surround',points,[tuple(range(4))],trim)
    # Lightguide follows the upper fender corner rather than an oval headlamp.
    tube('White LED daytime blade',[(side*.447,2.220,.783),(side*.641,2.139,.808),(side*.791,2.036,.818)],.010,white,8)
    tube('White LED projector bar',[(side*.506,2.222,.758),(side*.674,2.137,.784),(side*.782,2.065,.794)],.008,white,8)
    tube('Front amber marker',[(side*.798,2.044,.779),(side*.799,2.040,.808)],.007,amber,6)
    box('Outer bumper intake',(side*.668,2.150,.425),(.152,.026,.163),trim,.024)
    tube('Lower bumper silver garnish',[(side*.631,2.244,.328),(side*.764,2.185,.319),(side*.811,2.153,.357)],.009,alloy,6)
    rearpoints=[(side*.40,-2.203,.879),(side*.800,-2.100,.895),(side*.861,-1.991,.813),(side*.432,-2.221,.804)]
    mesh('Rear lamp dark backing',rearpoints,[tuple(reversed(range(4)))],trim)
    tube('Rear red LED outer signature',[(side*.433,-2.217,.864),(side*.707,-2.157,.875),(side*.834,-2.051,.846),(side*.811,-2.077,.815)],.014,red,8)
    tube('Rear red lower signature',[(side*.448,-2.226,.825),(side*.718,-2.160,.835)],.008,red,6)
    tube('Rear reverse lamp',[(side*.475,-2.230,.803),(side*.587,-2.208,.807)],.008,white,6)
    box('Rear bumper reflector',(side*.668,-2.230,.386),(.185,.014,.025),red,.009)
box('Rear lower diffuser',(0,-2.252,.273),(1.30,.040,.069),trim,.021)
for x in (-.57,.57):box('Tailpipe inset',(x,-2.274,.292),(.175,.014,.052),steel,.018)
tube('Rear trunk shutline',[(-.786,-1.690,.918),(-.70,-2.077,.900),(0,-2.202,.892),(.70,-2.077,.900),(.786,-1.690,.918)],.004,trim,6)
tube('Bonnet leading shutline',[(-.787,1.874,.809),(-.60,2.129,.778),(0,2.224,.742),(.60,2.129,.778),(.787,1.874,.809)],.0035,trim,6)
for side in (-1,1):tube('Bonnet pressed crease',[(side*.46,1.98,.811),(side*.44,1.40,.908),(side*.41,.98,.968)],.003,paint,5)
# Wipers, washer nozzles, roof aerial and fuel flap retain small-scale realism.
for x in (-.31,.31):tube('Windscreen wiper',[(x-.14,1.040,.988),(x,1.020,1.020),(x+.25,1.012,1.029)],.009,trim,6)
ellipsoid('Shark-fin aerial',(0,-.68,1.475),(.042,.100,.045),paint)
tube('Fuel filler outline',[(.880,-1.11,.775),(.877,-1.32,.775),(.877,-1.34,.898),(.880,-1.12,.899),(.880,-1.11,.775)],.003,trim,5)
for front in (True,False):
    yy=2.275 if front else -2.258;zz=.451 if front else .651
    box('Fictional registration surround',(0,yy,zz),(.418,.015,.168),trim,.009)
    box('Fictional registration enamel',(0,yy+(1 if front else -1)*.009,zz),(.393,.009,.148),plate,.005)
    curve=bpy.data.curves.new('Fictional plate characters','FONT');curve.body='KQF-0826';curve.align_x='CENTER';curve.align_y='CENTER';curve.size=.073;curve.extrude=.00025
    obj=bpy.data.objects.new('Fictional plate characters',curve);bpy.context.collection.objects.link(obj);obj.parent=root
    obj.location=(0,yy+(1 if front else -1)*.016,zz);obj.rotation_euler=(math.pi/2,0,math.pi if front else 0);obj.data.materials.append(trim)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.convert(target='MESH');obj.select_set(False)
anchor('Front grille',(0,2.27,.50));anchor('Rear plate',(0,-2.27,.64))

# Apply authored geometry then combine by material, retaining semantic anchors.
bpy.ops.object.select_all(action='DESELECT')
for o in list(bpy.data.objects):
    if o.type!='MESH':continue
    o.select_set(True);bpy.context.view_layer.objects.active=o
    for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
    o.select_set(False)
groups={}
for o in list(bpy.data.objects):
    if o.type=='MESH':groups.setdefault((o.parent.name,o.data.materials[0].name),[]).append(o)
for (parentname,name),items in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in items:o.select_set(True)
    bpy.context.view_layer.objects.active=items[0];bpy.ops.object.join();o=bpy.context.object;o.name=parentname+'_'+name
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    # Recalculate normals consistently after combining open and closed surfaces.
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.remove_doubles(threshold=.00005);bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
# One multi-material mesh per rotating wheel limits component count without
# merging wheel transforms into the body or making the brake calipers rotate.
for wheel in [o for o in root.children if o.type=='EMPTY' and o.name.startswith('Wheel_')]:
    items=[o for o in wheel.children if o.type=='MESH'];bpy.ops.object.select_all(action='DESELECT')
    for o in items:o.select_set(True)
    bpy.context.view_layer.objects.active=items[0];bpy.ops.object.join();bpy.context.object.name=wheel.name+'_Assembly'
bpy.ops.object.select_all(action='DESELECT')
base_meshes=[o for o in bpy.data.objects if o.type=='MESH']
def triangles(objects):return sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
def bounds(objects):
    bpy.context.view_layer.update();pts=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
    lo=[min(p[i] for p in pts) for i in range(3)];hi=[max(p[i] for p in pts) for i in range(3)]
    return lo,hi,[hi[i]-lo[i] for i in range(3)]

# Budget each level independently. Identical materials and anchors permit Unity
# to use a single LODGroup for moving hazards and parked-car instances.
base_count=triangles(base_meshes)
if base_count>65000:
    for o in base_meshes:
        if len(o.data.polygons)>60:
            bpy.context.view_layer.objects.active=o;m=o.modifiers.new('LOD0 topology budget','DECIMATE');m.ratio=62000/base_count;bpy.ops.object.modifier_apply(modifier=m.name)
lod_reports=[]
lod_objects=[]
for level,target_triangles in enumerate((None,12000,5000)):
    if level==0:meshes=base_meshes;model_root=root
    else:
        model_root=bpy.data.objects.new('CompactSedan_Model_LOD'+str(level),None);bpy.context.collection.objects.link(model_root)
        parent_map={root:model_root}
        for marker in [o for o in root.children_recursive if o.type=='EMPTY']:
            copy=marker.copy();bpy.context.collection.objects.link(copy);copy.parent=parent_map.get(marker.parent,model_root);copy.name=marker.name+'_LOD'+str(level);parent_map[marker]=copy
        meshes=[];ratio=target_triangles/max(1,triangles(base_meshes))
        for original in base_meshes:
            o=original.copy();o.data=original.data.copy();bpy.context.collection.objects.link(o);o.parent=parent_map.get(original.parent,model_root);meshes.append(o)
            local_min=[min(v.co[i] for v in original.data.vertices) for i in range(3)];local_max=[max(v.co[i] for v in original.data.vertices) for i in range(3)]
            if len(o.data.polygons)>20:
                bpy.context.view_layer.objects.active=o;m=o.modifiers.new('Measured LOD reduction','DECIMATE');m.ratio=ratio;bpy.ops.object.modifier_apply(modifier=m.name)
                for v in o.data.vertices:
                    for axis in range(3):v.co[axis]=max(local_min[axis],min(local_max[axis],v.co[axis]))
    # Low-LOD reduction can collapse a glazing shell onto its inner face. Glass
    # is opaque exterior glazing in the game: explicitly preserve its exterior
    # winding after simplification so single-sided URP rendering stays intact.
    # Mirror lenses sit outside this cabin range and keep their curved normals.
    bpy.context.view_layer.update()
    for o in meshes:
        if not any(m and m.name=='SedanGlass' for m in o.data.materials):continue
        normal_matrix=o.matrix_world.to_3x3().inverted().transposed()
        for face in o.data.polygons:
            if o.data.materials[face.material_index].name!='SedanGlass':continue
            centre=o.matrix_world@face.center
            if abs(centre.x)<.82 and (normal_matrix@face.normal).dot(centre-Vector((0,-.2,1.10)))<0:face.flip()
        o.data.update()
    bpy.ops.object.select_all(action='DESELECT');model_root.select_set(True)
    for o in model_root.children_recursive:o.select_set(True)
    name='CompactSedan_Detailed_LOD'+str(level)
    # Blender requires unique scene names, but each exported file can have the
    # same stable anchor names. Temporarily free the LOD0 names for each export.
    rename_restore=[]
    if level>0:
        for original in [o for o in root.children_recursive if o.type=='EMPTY']:
            exported_name=original.name;copy=parent_map[original]
            rename_restore.append((original,exported_name,copy,copy.name));original.name=exported_name+'__source';copy.name=exported_name
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=False,add_leaf_bones=False,path_mode='STRIP')
    for original,exported_name,copy,copy_name in rename_restore:copy.name=copy_name;original.name=exported_name
    lo,hi,size=bounds(meshes)
    lod_reports.append({'level':level,'file':name+'.fbx','triangles':triangles(meshes),'renderers':len(meshes),'boundsMin':lo,'boundsMax':hi,'dimensions':size,'finiteGeometry':all(math.isfinite(c) for o in meshes for v in o.data.vertices for c in v.co)})
    lod_objects.append((model_root,meshes))
    if level>0:
        for o in [model_root]+list(model_root.children_recursive):o.hide_render=True;o.hide_set(True)
material_report={'asset':'CompactSedan_Detailed','originalDesign':True,'colorEncoding':'linear','materials':[{'name':m.name,'color':list(m.diffuse_color),'metallic':m.node_tree.nodes['Principled BSDF'].inputs['Metallic'].default_value,'roughness':m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value} for m in bpy.data.materials]}
(SOURCE/'CompactSedan_Detailed-materials.json').write_text(json.dumps(material_report,indent=2),encoding='utf-8')
report={'asset':'Original contemporary compact sedan','authoringAxes':'X lateral; Y forward; Z up','metres':True,'wheelbase':2.66,'wheelRadius':.318,'wheelPivots':['Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR'],'wheelAnimation':'Each FBX has exact Wheel_FL/FR/RL/RR names. Each wheel owns one multi-material mesh and rotates around local X.','bodyLength':4.50,'bodyWidth':1.77,'colliderIntegration':'Keep original 1.78 x 1.42 x 4.52 m collider; mirror tips, small roof aerial and plate margins are decorative overhangs. Do not generate model colliders.','frontAnchor':[0,2.27,.50],'rearAnchor':[0,-2.27,.64],'lods':lod_reports,'checks':{'finiteAll':all(x['finiteGeometry'] for x in lod_reports),'frontDirection':True,'wheelbase':abs((1.30-(-1.36))-2.66)<1e-6,'tyresTouchGround':abs(lod_reports[0]['boundsMin'][2])<.003,'LOD0Budget':35000<=lod_reports[0]['triangles']<=65000,'LOD1Budget':8500<=lod_reports[1]['triangles']<=15000,'LOD2Budget':3000<=lod_reports[2]['triangles']<=6500,'materialBatchBudget':all(x['renderers']<=28 for x in lod_reports)},'integration':['Use exact Front grille / Rear plate anchors in every FBX to correct forward orientation.','Ten shared URP materials, with separate per-wheel batches to permit rotation.','Static cars and cut-in actor share LOD0/LOD1/LOD2 assets and existing collision proxies.','KQF-0826 is an invented decorative plate, no brand logos.']}
(CHECK/'model-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'CompactSedan_Detailed.blend'))
print('CAR_EXPORTED '+json.dumps(report['lods']),flush=True)
if '--no-preview' in sys.argv:
    assert all(report['checks'].values())
    sys.exit(0)

# A single modest studio render, after export. Studio objects are not game assets.
box('Studio floor',(0,0,-.075),(200,200,.12),mat('StudioFloor',(.15,.18,.20),0,.75))
world=bpy.context.scene.world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.32,.38,.43,1);world.node_tree.nodes['Background'].inputs[1].default_value=.55
for name,loc,energy,size in [('Key',(2.5,4,7),1500,5),('Fill',(-5,2,4),1200,5),('Rim',(1,-5,5),1800,4)]:
    d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.shape='DISK';d.size=size;o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.7))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Preview');o=bpy.data.objects.new('Preview',d);bpy.context.collection.objects.link(o);o.location=(6.8,7.8,3.8);o.rotation_euler=(Vector((0,0,.74))-o.location).to_track_quat('-Z','Y').to_euler();d.lens=54
scene=bpy.context.scene;scene.camera=o;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.resolution_x=1200;scene.render.resolution_y=800;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(CHECK/'compact-sedan-preview.png')
bpy.ops.render.render(write_still=True)
assert all(report['checks'].values()),'Model validation failed: '+json.dumps(report['checks'])
print('CAR_COMPLETE '+json.dumps(report['checks']),flush=True)
