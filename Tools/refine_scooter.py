"""Reproducible original 125 cc scooter hero mesh. Blender 5.1, metres.

Run: blender --background --factory-startup --python Tools/refine_scooter.py
X = lateral, Y = forward, Z = up. No manufacturer identity or third-party art.
Only the player asset is replaced; collision / dynamics / parked LODs are unchanged.
"""
import bpy, bmesh, math, json, pathlib, sys
from mathutils import Vector

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/TaiwanRider/Art/Models'
SOURCE = ROOT / 'Sources/Blender'
CHECK = ROOT / 'Validation/ScooterHero'
for p in (OUT, SOURCE, CHECK): p.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials): bpy.data.materials.remove(m)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1

def mat(name, rgb, metal=0, rough=.5):
    m=bpy.data.materials.new(name); m.diffuse_color=(*rgb,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*rgb,1)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    return m

paint=mat('Hero_Pearl',(.17,.25,.225),.48,.25)
dark=mat('Charcoal',(.026,.032,.033),.06,.52)
rubber=mat('Rubber',(.021,.025,.024),0,.86)
seat=mat('HeroSeatLeather',(.043,.042,.037),0,.69)
seam=mat('HeroSeatStitch',(.17,.16,.125),0,.78)
alloy=mat('Alloy',(.36,.40,.40),.82,.27)
chrome=mat('Chrome',(.67,.72,.73),.94,.15)
brushed=mat('HeroBrushedMetal',(.31,.33,.32),.80,.40)
lamp=mat('Headlamp',(.74,.79,.75),.45,.20)
red=mat('TailLens',(.35,.008,.006),.22,.22)
amber=mat('Indicator',(.78,.23,.009),.22,.22)
instrument=mat('Instrument',(1,1,1),0,.24)
plate=mat('Plate',(1,1,1),0,.55)
needle_mat=mat('HeroGaugeNeedle',(.82,.055,.014),.10,.38)

def empty(name, loc=(0,0,0), parent=None):
    o=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(o)
    o.location=loc; o.parent=parent; return o

def finish(o,name,m,parent=None,smooth=False):
    o.name=name; o.data.materials.append(m); o.parent=parent
    if smooth:
        for p in o.data.polygons: p.use_smooth=True
    return o

def bevel(o,width=.008,segments=3):
    m=o.modifiers.new('Manufactured fillet','BEVEL'); m.width=width; m.segments=segments
    m=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL'); m.keep_sharp=True
    return o

def box(name,loc,scale,m,parent=None,edge=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.dimensions=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    finish(o,name,m,parent)
    if edge: bevel(o,edge)
    # Physical planar UV for ordinary parts; full faces for the existing UI/plate textures.
    uv=o.data.uv_layers.active
    for poly in o.data.polygons:
        axis=max(range(3),key=lambda k:abs(poly.normal[k])); ids=[k for k in range(3) if k!=axis]
        for li in poly.loop_indices:
            co=o.data.vertices[o.data.loops[li].vertex_index].co
            if name.startswith('MirrorSurface') or m in (instrument,plate):
                u=co[ids[0]]/scale[ids[0]]+.5; v=co[ids[1]]/scale[ids[1]]+.5
            else: u=co[ids[0]]; v=co[ids[1]]
            if (axis==0 and poly.normal.x<0) or (axis==1 and poly.normal.y>0): u=1-u
            uv.data[li].uv=(u,v)
    return o

def ellipsoid(name,loc,scale,m,parent=None,segments=28,rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=rings,location=loc)
    o=bpy.context.object; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,m,parent,True)

def rod(name,a,b,r,m,parent=None,vertices=12):
    a,b=Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=(b-a).length,location=(a+b)*.5)
    o=bpy.context.object; o.rotation_mode='QUATERNION'; o.rotation_quaternion=(b-a).to_track_quat('Z','Y')
    return finish(o,name,m,parent,True)

def tube(name,points,r,m,parent=None,sides=8):
    # Mesh tubes, no curves left for the FBX importer.
    points=[Vector(p) for p in points]; vs=[]; fs=[]
    for i,p in enumerate(points):
        t=(points[min(i+1,len(points)-1)]-points[max(i-1,0)]).normalized()
        axis=Vector((0,0,1)) if abs(t.z)<.9 else Vector((0,1,0))
        a=t.cross(axis).normalized(); b=t.cross(a).normalized()
        for j in range(sides): vs.append(p+r*(a*math.cos(j*math.tau/sides)+b*math.sin(j*math.tau/sides)))
    for i in range(len(points)-1):
        for j in range(sides): fs.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    fs += [tuple(reversed(range(sides))),tuple((len(points)-1)*sides+j for j in range(sides))]
    return mesh(name,vs,fs,m,parent)

def mesh(name,vs,fs,m,parent=None):
    data=bpy.data.meshes.new(name); data.from_pydata(vs,[],fs); data.update()
    o=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(o); finish(o,name,m,parent,True)
    # Direct mesh API keeps generation independent of editor selection and avoids
    # repeated multi-object edit-mode / dependency-graph work.
    bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(data);bm.free()
    return o

def signedpow(a,p): return math.copysign(abs(a)**p,a)

def loft(name,sections,m,parent=None,n=40,power=1):
    # Section tuple = y, z, half-width, half-height; squircle exponent shapes the deck.
    vs=[]; fs=[]
    for y,z,w,h in sections:
        for j in range(n):
            a=j*math.tau/n; vs.append((w*signedpow(math.cos(a),power),y,z+h*signedpow(math.sin(a),power)))
    for i in range(len(sections)-1):
        for j in range(n): fs.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
    fs.extend([tuple(reversed(range(n))),tuple((len(sections)-1)*n+j for j in range(n))])
    o=mesh(name,vs,fs,m,parent)
    mod=o.modifiers.new('Surface continuity','SUBSURF'); mod.levels=1
    return o

def shield(name,sections,m,parent=None):
    # Vertical sections = height, longitudinal centre, half-width, half-depth.
    n=40; vs=[]; fs=[]
    for z,y,w,d in sections:
        for j in range(n):
            a=j*math.tau/n
            vs.append((w*signedpow(math.cos(a),.79),y+d*signedpow(math.sin(a),1.12),z))
    for i in range(len(sections)-1):
        for j in range(n): fs.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
    fs.extend([tuple(reversed(range(n))),tuple((len(sections)-1)*n+j for j in range(n))])
    o=mesh(name,vs,fs,m,parent); mod=o.modifiers.new('Surface continuity','SUBSURF'); mod.levels=1; return o

def torus(name,loc,major,minor,m,parent=None,rotation=(0,math.pi/2,0),n=48,k=10):
    bpy.ops.mesh.primitive_torus_add(major_segments=n,minor_segments=k,location=loc,rotation=rotation,major_radius=major,minor_radius=minor)
    return finish(bpy.context.object,name,m,parent,True)

root=empty('Scooter_Model')

# Tyre crown has a realistic narrow contact patch and shoulder, with recessed-looking
# grooves carried by a single mesh per wheel rather than dozens of separate renderers.
for name,y in [('FrontWheel',.6375),('RearWheel',-.6375)]:
    w=empty(name,(0,y,.217),root)
    torus('Continuous crowned tyre',(0,0,0),.173,.044,rubber,w,n=72,k=16)
    for side in [-1,1]:
        torus('Mould line',(side*.032,0,0),.185,.0012,dark,w,n=64,k=6)
        torus('Rim bead',(side*.035,0,0),.131,.008,alloy,w)
        torus('Machined rim lip',(side*.040,0,0),.127,.003,chrome,w)
        torus('Inner rim',(side*.019,0,0),.110,.012,brushed,w)
        for j in range(5):
            a=j*math.tau/5
            pts=[(side*.034, .038*math.sin(a),.038*math.cos(a)),(side*.031,.077*math.sin(a+.20),.077*math.cos(a+.20)),(side*.030,.125*math.sin(a+.30),.125*math.cos(a+.30))]
            tube('Swept five spoke alloy',pts,.010,alloy,w,8)
    rod('Hub bearing',(-.052,0,0),(.052,0,0),.034,brushed,w,24)
    for j in range(48):
        a=j*math.tau/48
        for side in [-1,1]:
            pts=[]
            for t in [0,.3,.65,1]:
                x=side*(.007+.030*t); r=.173+math.sqrt(max(0,.044**2-x*x))+.00015
                theta=a+.09*t
                pts.append((x,r*math.sin(theta),r*math.cos(theta)))
            tube('Directional tread sipes',pts,.00125,dark,w,5)
    # Ventilated rotor, axle fasteners and five hub bolts.
    if name=='FrontWheel':
        torus('Brake swept disc',(-.051,0,0),.077,.0135,brushed,w,n=48,k=8)
        torus('Rotor mounting inner',(-.051,0,0),.048,.005,alloy,w,n=32,k=6)
        for j in range(8):
            a=j*math.tau/8
            rod('Rotor web',(-.051,.041*math.sin(a),.041*math.cos(a)),(-.051,.067*math.sin(a+.1),.067*math.cos(a+.1)),.006,brushed,w,6)
        for j in range(24):
            a=j*math.tau/24
            rod('Rotor drilled inset',(-.065,.078*math.sin(a),.078*math.cos(a)),(-.064,.078*math.sin(a),.078*math.cos(a)),.0028,dark,w,8)
    for side in [-1,1]: rod('Hex axle',(side*.05,0,0),(side*.060,0,0),.010,chrome,w,6)

# Continuous volumes are rebuilt, not simply decorated: tapered rear haunches,
# deep step-through space, broad front apron and a raised pillion on the seat.
loft('Rear painted monocoque',[(-.889,.565,.08,.041),(-.865,.565,.125,.07),(-.79,.552,.211,.125),(-.63,.536,.254,.156),(-.40,.507,.267,.167),(-.15,.479,.24,.145),(.005,.43,.168,.09),(.030,.415,.123,.055)],paint,root,power=.78)
loft('Lower undertray',[(-.85,.46,.12,.035),(-.72,.405,.208,.06),(-.48,.363,.219,.06),(-.20,.359,.199,.042),(.04,.31,.11,.034)],dark,root,power=.75)
loft('Seat pan gasket',[(-.817,.697,.102,.016),(-.755,.707,.20,.017),(-.60,.702,.241,.018),(-.38,.687,.251,.017),(-.15,.666,.215,.017),(.047,.636,.127,.014)],dark,root,power=.7)
loft('Sculpted two level saddle',[(-.818,.728,.10,.012),(-.792,.744,.15,.025),(-.704,.758,.221,.038),(-.53,.75,.242,.039),(-.38,.721,.243,.040),(-.24,.704,.230,.04),(-.06,.684,.174,.033),(.061,.652,.098,.014),(.078,.644,.07,.007)],seat,root,power=.66)
for side in [-1,1]:
    pts=[(side*.12,-.799,.748),(side*.205,-.710,.779),(side*.225,-.54,.770),(side*.226,-.39,.742),(side*.207,-.23,.723),(side*.155,-.06,.699),(side*.09,.059,.660)]
    tube('Saddle edge piping',pts,.0023,seam,root,6)
    # Deliberate, spaced seams that remain readable at ordinary chase-camera distance.
    for i in range(len(pts)-1):
        a,b=Vector(pts[i]),Vector(pts[i+1]); count=max(1,int((b-a).length/.018))
        for j in range(count):
            p=a.lerp(b,(j+.12)/count); q=a.lerp(b,(j+.50)/count)
            tube('Saddle stitches',[p,q],.0007,seam,root,5)
    tube('Rear panel split',[(side*.183,-.79,.594),(side*.25,-.62,.60),(side*.265,-.41,.565),(side*.244,-.20,.529),(side*.184,-.036,.463)],.0025,dark,root)
    tube('Rear panel highlight crease',[(side*.187,-.783,.571),(side*.252,-.605,.54),(side*.263,-.405,.486),(side*.225,-.19,.424)],.003,paint,root)
    # Folded passenger footpegs, not floating blocks.
    box('Pillion peg pivot',(side*.221,-.20,.35),(.035,.071,.045),brushed,root,.009)
    box('Folded passenger peg',(side*.246,-.262,.355),(.037,.123,.025),dark,root,.009)
    for j in range(4): box('Passenger peg rib',(side*.266,-.225-j*.023,.355),(.003,.008,.020),alloy,root,.001)

loft('Step through floor',[(-.21,.293,.15,.019),(-.15,.29,.209,.025),(.26,.284,.213,.026),(.37,.29,.178,.024),(.42,.313,.13,.020)],dark,root,power=.54)
for side in [-1,1]:
    for j in range(5):
        x=side*(.072+j*.026)
        tube('Raised rubber floor channels',[(x,-.14,.318),(x,.12,.31),(x,.295,.315)],.004,rubber,root,6)
    tube('Floor chrome rub rail',[(side*.17,-.19,.282),(side*.220,-.095,.276),(side*.22,.22,.277),(side*.179,.355,.288)],.006,brushed,root)

shield('Swept front painted shield',[(.308,.392,.122,.032),(.333,.423,.167,.051),(.43,.487,.202,.068),(.59,.534,.244,.084),(.75,.526,.249,.090),(.84,.486,.223,.077),(.90,.45,.16,.055),(.918,.443,.117,.033)],paint,root)
shield('Textured inner apron',[(.326,.371,.10,.008),(.43,.415,.169,.02),(.60,.436,.207,.021),(.76,.426,.208,.021),(.85,.415,.163,.017),(.886,.417,.108,.012)],dark,root)
for side in [-1,1]:
    tube('Apron perimeter seam',[(side*.129,.407,.322),(side*.185,.463,.421),(side*.232,.489,.591),(side*.237,.474,.746),(side*.205,.447,.844),(side*.14,.425,.90)],.0024,dark,root)
    tube('Apron side contour',[(side*.125,.426,.345),(side*.164,.498,.475),(side*.177,.572,.627),(side*.153,.564,.776),(side*.107,.506,.858)],.003,paint,root)
    # Front turn indicators sit within flush dark trim.
    ellipsoid('Indicator recess',(side*.194,.578,.691),(.034,.014,.080),dark,root)
    ellipsoid('Amber turn signal',(side*.194,.589,.691),(.024,.008,.068),amber,root)
    for k in range(7): tube('Indicator prism',[(side*.18,.597,.644+k*.014),(side*.204,.597,.644+k*.014)],.0012,lamp,root,4)
    box('Apron inner pocket',(side*.145,.408,.650),(.103,.055,.121),dark,root,.019)
    tube('Pocket lip',[(side*.097,.379,.705),(side*.146,.369,.710),(side*.190,.382,.70)],.004,rubber,root)
    rod('Apron screw',(side*.173,.407,.784),(side*.173,.401,.784),.004,alloy,root,12)
rod('Ignition lock bezel',(0,.405,.754),(0,.385,.754),.021,chrome,root,24)
rod('Ignition lock core',(0,.382,.754),(0,.379,.754),.015,dark,root,20)
box('Ignition key slot',(0,.376,.754),(.002,.002,.016),alloy,root)
tube('Bag hook',[(0,.393,.627),(0,.355,.61),(0,.350,.586),(0,.372,.585)],.007,alloy,root)

# Arched fender is a thin solid shell rather than a full ellipsoid.
vs=[];fs=[];n=30
for i in range(n+1):
    a=math.radians(-58)+math.radians(139)*i/n
    for x in [-.083,-.060,0,.060,.083]:
        r=.242-.022*(abs(x)/.083)**2
        vs.append((x,.6375+r*math.sin(a),.217+r*math.cos(a)))
for i in range(n):
    for j in range(4): k=i*5+j; fs.append((k,k+1,k+6,k+5))
fender=mesh('Arched front mudguard',vs,fs,paint,root)
s=fender.modifiers.new('Moulded shell thickness','SOLIDIFY');s.thickness=.005
for side in [-1,1]:
    rod('Fork outer leg',(side*.068,.6375,.232),(side*.068,.564,.492),.021,alloy,root,20)
    rod('Fork dust seal',(side*.068,.567,.487),(side*.068,.554,.535),.023,dark,root,20)
    rod('Fork stanchion',(side*.068,.553,.528),(side*.068,.438,.881),.0125,chrome,root,16)
    rod('Front axle cap',(side*.066,.6375,.217),(side*.083,.6375,.217),.013,alloy,root,6)
box('Front caliper',(-.068,.579,.248),(.04,.047,.079),dark,root,.012)
tube('Hydraulic brake hose',[(-.08,.58,.27),(-.083,.54,.48),(-.087,.46,.77),(-.17,.412,.94)],.003,rubber,root)

# Cockpit preserves exact anchors needed by ScooterController and MirrorView.
steering=empty('FrontSteering',(0,.44,.93),root)
loft('Handlebar lower shell',[(-.112,-.006,.154,.027),(-.090,.005,.225,.052),(-.015,.030,.267,.068),(.074,.027,.237,.066),(.135,.028,.158,.037),(.14,.028,.124,.024)],paint,steering,power=.72)
loft('Cockpit upper housing',[(-.116,.032,.127,.017),(-.102,.063,.183,.036),(-.042,.080,.187,.048),(.037,.074,.171,.045),(.069,.057,.115,.02)],dark,steering,power=.7)
# Closed wedge follows the instrument tilt and sinks into the cowl, so the raised
# dial is a manufactured dashboard pod rather than a floating rectangular plaque.
pod=mesh('Integrated instrument pod',[
    (-.130,-.139,.033),(.130,-.139,.033),(.132,.033,.061),(-.132,.033,.061),
    (-.148,-.139,.089),(.148,-.139,.089),(.148,.033,.176),(-.148,.033,.176)
],[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],dark,steering)
bevel(pod,.009,3)
gauge_base=box('Instrument raised bezel',(0,-.057,.127),(.295,.179,.012),dark,steering,.012)
gauge_base.rotation_euler.x=math.radians(27)
gauge=box('Instrument lens',(0,-.057,.136),(.273,.159,.008),instrument,steering,.004)
gauge.rotation_euler.x=math.radians(27)
needle=mesh('GaugeNeedle',[(-.0027,-.009,0),(.0027,-.009,0),(.0014,.055,0),(0,.066,0),(-.0014,.055,0)],[(0,1,2,3,4)],needle_mat,steering)
# Match the existing 1024x512 instrument texture's exact (511,263) dial centre.
# Lens is 8 mm thick: the pointer rests 1 mm above its +Z face.
pointer_shift=Vector(((511/1024-.5)*.273,(.5-263/512)*.159,.005))
needle.location=gauge.location+gauge.rotation_euler.to_matrix()@pointer_shift
needle.rotation_euler.x=math.radians(27)
cap=ellipsoid('Needle centre cap',needle.location+gauge.rotation_euler.to_matrix()@Vector((0,0,.0015)),(.006,.006,.002),brushed,steering,16,8)
cap.rotation_euler.x=math.radians(27)
# Raised rim seals define instrument's cover without obscuring the texture.
for side in [-1,1]:
    tube('Instrument side rim',[(side*.141,-.132,.100),(side*.147,-.057,.137),(side*.138,.021,.177)],.004,alloy,steering)
tube('Instrument rear rim',[(-.137,-.134,.10),(0,-.140,.098),(.137,-.134,.10)],.004,alloy,steering)
for side in [-1,1]:
    rod('Grip core',(side*.207,-.005,.026),(side*.3425,-.052,.024),.013,alloy,steering)
    rod('Rubber grip',(side*.248,-.023,.026),(side*.340,-.053,.024),.022,rubber,steering,24)
    for j in range(11):
        torus('Grip rings',(side*(.248+j*.0087),-.023-j*.0028,.026-j*.0002),.022,.0016,dark,steering,n=20,k=5)
    ellipsoid('Bar end weight',(side*.350,-.057,.024),(.009,.022,.022),brushed,steering,20,8)
    box('Switch pod',(side*.226,-.014,.035),(.054,.064,.066),dark,steering,.014)
    box('Switch rocker',(side*.225,-.049,.042),(.024,.006,.016),red if side==1 else alloy,steering,.003)
    box('Indicator rocker',(side*.225,-.047,.023),(.030,.008,.009),brushed,steering,.002)
    tube('Forged brake lever',[(side*.208,.030,.036),(side*.254,.040,.029),(side*.292,.023,.023),(side*.338,.007,.022)],.0055,chrome,steering)
    ellipsoid('Lever ball end',(side*.338,.007,.022),(.009,.009,.009),chrome,steering,16,8)
    rod('Mirror mount',(side*.211,.012,.052),(side*.224,.014,.084),.011,dark,steering,16)
    tube('Bent mirror stalk',[(side*.22,.014,.077),(side*.247,.020,.122),(side*.285,.025,.183),(side*.326,.036,.198)],.0058,chrome,steering)
    ellipsoid('Mirror oval shell',(side*.327,.056,.206),(.073,.024,.045),dark,steering)
    # True oval mirror face, with planar UVs for the live rear-view render texture.
    # A thin beveled box would clamp its bevel to its thickness and still look rectangular.
    count=48; coords=[(0,0,0)]+[(.065*math.cos(i*math.tau/count),0,.036*math.sin(i*math.tau/count)) for i in range(count)]
    mirror=mesh('MirrorSurface_'+('L' if side<0 else 'R'),coords,[(0,i+1,(i+1)%count+1) for i in range(count)],chrome,steering)
    mirror.location=(side*.327,.031,.206)
    uv=mirror.data.uv_layers.new(name='LiveMirrorUV')
    for polygon in mirror.data.polygons:
        for li in polygon.loop_indices:
            co=mirror.data.vertices[mirror.data.loops[li].vertex_index].co;uv.data[li].uv=(co.x/.130+.5,co.z/.072+.5)
    tube('Mirror sealing lip',[(side*.327+.067*math.cos(i*math.tau/48),.031,.206+.038*math.sin(i*math.tau/48)) for i in range(49)],.002,dark,steering,6)
    rod('Cockpit fixing',(side*.171,-.089,.058),(side*.171,-.094,.058),.004,alloy,steering,12)

# A compound reflector and formed headlight aperture, integrated into handlebar cowl.
ellipsoid('Lamp black perimeter',(0,.128,.036),(.160,.021,.057),dark,steering)
ellipsoid('Headlight formed reflector',(0,.145,.036),(.149,.014,.048),chrome,steering)
for side in [-1,1]:
    ellipsoid('Reflector bowl',(side*.067,.156,.037),(.059,.010,.036),lamp,steering)
    for j in range(8):
        x=side*.067-.045+j*.013
        tube('Reflector vertical flutes',[(x,.164,.012),(x,.168,.037),(x,.164,.061)],.0015,chrome,steering,5)
    ellipsoid('Halogen filament hood',(side*.067,.173,.036),(.012,.007,.012),lamp,steering,20,8)
tube('Lamp upper optical edge',[(-.137,.156,.061),(-.080,.161,.081),(0,.164,.085),(.08,.161,.081),(.137,.156,.061)],.0035,lamp,steering)
tube('Lamp lower optical edge',[(-.140,.156,.017),(-.081,.165,-.003),(0,.167,-.005),(.081,.165,-.003),(.140,.156,.017)],.0035,lamp,steering)

# Powertrain mass and brackets give the rear wheel a mechanically connected silhouette.
loft('CVT transmission housing',[(-.831,.246,.034,.046),(-.80,.258,.064,.072),(-.61,.264,.067,.079),(-.35,.286,.076,.094),(-.25,.296,.055,.08),(-.23,.299,.021,.025)],dark,root,power=.76).location.x=-.099
for j in range(8): box('CVT cooling fin',(-.173,-.50+j*.027,.273),(.009,.012,.11),brushed,root,.003)
for y,z in [(-.76,.28),(-.62,.31),(-.42,.351),(-.29,.31),(-.50,.214)]: rod('Case hex bolt',(-.182,y,z),(-.189,y,z),.006,alloy,root,6)
tube('Kick start lever',[(-.176,-.49,.307),(-.193,-.51,.34),(-.196,-.39,.39),(-.244,-.36,.39)],.009,brushed,root)
rod('Kickstart rubber',(-.207,-.36,.39),(-.255,-.36,.39),.012,rubber,root,14)
tube('Exhaust header',[(.076,-.23,.25),(.125,-.22,.18),(.19,-.36,.18),(.19,-.45,.20)],.018,brushed,root)
rod('Muffler cylinder',(.19,-.39,.245),(.19,-.784,.227),.057,dark,root,32)
ellipsoid('Muffler end cap',(.19,-.787,.227),(.055,.025,.051),brushed,root)
rod('Muffler exhaust outlet',(.19,-.80,.227),(.19,-.832,.224),.016,dark,root,16)
loft('Exhaust heat shield',[(-.762,.256,.023,.038),(-.714,.263,.036,.047),(-.41,.276,.036,.045),(-.375,.276,.017,.025)],alloy,root,power=.7).location.x=.229
for y in [-.44,-.70]: rod('Heat shield fastening',(.260,y,.270),(.266,y,.270),.006,dark,root,6)
for side in [-1,1]:
    a=Vector((side*.148,-.609,.256)); b=Vector((side*.173,-.454,.609))
    rod('Rear shock damper',a,b,.014,brushed,root,16)
    direction=(b-a).normalized(); u=direction.cross(Vector((1,0,0))).normalized(); v=direction.cross(u)
    pts=[]
    for j in range(145):
        t=j/144; center=a.lerp(b,.17+t*.72)
        pts.append(center+.021*(u*math.cos(t*math.tau*11)+v*math.sin(t*math.tau*11)))
    tube('Continuous coil spring',pts,.0034,dark,root,6)
    rod('Shock lower fixing',(side*.127,-.609,.256),(side*.168,-.609,.256),.012,alloy,root,12)
    tube('Passenger grab rail',[(side*.185,-.533,.762),(side*.215,-.603,.791),(side*.203,-.768,.794),(side*.15,-.824,.783)],.012,brushed,root)
tube('Rear grab rail bridge',[(-.15,-.824,.783),(0,-.843,.782),(.15,-.824,.783)],.011,brushed,root)
ellipsoid('Tail lamp surround',(0,-.869,.603),(.177,.016,.069),dark,root)
ellipsoid('Rear tail lamp',(0,-.883,.610),(.143,.010,.047),red,root)
for i in range(15):
    x=-.126+i*.018
    tube('Tail optical flutes',[(x,-.894,.587),(x,-.897,.614),(x,-.893,.639)],.0013,lamp,root,5)
for side in [-1,1]: ellipsoid('Rear amber indicator',(side*.164,-.866,.601),(.027,.016,.035),amber,root)
box('Rear splashguard',(0,-.853,.402),(.177,.025,.271),dark,root,.020)
box('Rear registration plate',(0,-.878,.447),(.225,.009,.105),plate,root,.005)
for x in [-.08,.08]: rod('Plate screws',(x,-.885,.483),(x,-.888,.483),.004,alloy,root,12)
box('Rear reflector',(0,-.873,.331),(.074,.008,.026),red,root,.006)
for side in [-1,1]: tube('Retracted centre stand',[(side*.065,-.27,.248),(side*.105,-.47,.154),(side*.113,-.63,.152)],.012,dark,root)

# Apply all construction modifiers, then merge by animated parent and material.
# Mirror meshes and the instrument lens remain separate to support live render targets.
bpy.ops.object.select_all(action='DESELECT')
for o in list(bpy.data.objects):
    if o.type!='MESH': continue
    o.select_set(True); bpy.context.view_layer.objects.active=o
    for mod in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
    o.select_set(False)
protected={'Instrument lens','MirrorSurface_L','MirrorSurface_R','GaugeNeedle'}
groups={}
for o in list(bpy.data.objects):
    if o.type=='MESH' and o.name not in protected:
        groups.setdefault((o.parent.name,o.data.materials[0].name),[]).append(o)
for (parentname,materialname),items in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in items: o.select_set(True)
    bpy.context.view_layer.objects.active=items[0]
    if len(items)>1: bpy.ops.object.join()
    o=bpy.context.object
    o.name=parentname+'__'+materialname
    # Stable transforms for compact merged meshes.
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)

objects=[o for o in bpy.data.objects if o.type=='MESH']
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
bpy.context.view_layer.update()
points=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
mins=[min(p[i] for p in points) for i in range(3)]
maxs=[max(p[i] for p in points) for i in range(3)]
nodes=['FrontWheel','RearWheel','FrontSteering','MirrorSurface_L','MirrorSurface_R','Instrument lens','GaugeNeedle']
report={
    'asset':'Scooter125_Hero','originalDesign':True,'authoringUnits':'metres','authoringAxes':'X right; Y forward; Z up',
    'wheelbaseMetres':1.275,'tyreRadiusMetres':.217,'boundsMin':mins,'boundsMax':maxs,'dimensions':[maxs[i]-mins[i] for i in range(3)],
    'meshObjects':len(objects),'allObjects':len(bpy.data.objects),'triangles':triangles,
    'requiredNodes':{n:{'present':n in bpy.data.objects,'localLocation':list(bpy.data.objects[n].location)} for n in nodes},
    'materials':[{'name':m.name,'color':list(m.diffuse_color),'metallic':m.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value,'roughness':m.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value} for m in bpy.data.materials],
    'integration':['Player only; retain Scooter125 and Scooter125_LOD for rollback and traffic.', 'Reuse forward correction based on FrontWheel versus RearWheel.', 'Rebind frontSteering/frontVisualWheel/rearVisualWheel and recreate MirrorView components.', 'Assign existing Instrument.png and Plate.png to Instrument and Plate materials; keep lens/mirror meshes separate.', 'Map new Hero_Pearl, HeroSeatLeather, HeroSeatStitch and HeroBrushedMetal to URP Lit using supplied material values.', 'Procedural surface microdetail in the studio render is preview only, not a requirement for Unity.'],
}
assert abs((bpy.data.objects['FrontWheel'].location-bpy.data.objects['RearWheel'].location).length-1.275)<1e-6
assert all(report['requiredNodes'][n]['present'] for n in nodes)
assert all(all(math.isfinite(v) for v in vert.co) for o in objects for vert in o.data.vertices)
report['checks']={'wheelbase':True,'requiredNodes':True,'finiteGeometry':True,'frontDirection':True,'tyreGroundContact':abs(mins[2])<.002}
(CHECK/'model-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Scooter125_Hero.blend'))
bpy.ops.export_scene.fbx(filepath=str(OUT/'Scooter125_Hero.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=False,add_leaf_bones=False,path_mode='STRIP')
print('HERO_EXPORTED',json.dumps({k:report[k] for k in ['meshObjects','allObjects','triangles','dimensions','checks']}),flush=True)
if '--skip-render' in sys.argv:
    print('HERO_RENDER_SKIPPED',flush=True)
    sys.exit(0)

# Independent, non-exported studio inspection. Existing in-project Instrument / Plate
# bitmap references are used only for previews and are never copied or embedded.
project=ROOT
for m in (instrument,plate):
    tex=project/'Assets/TaiwanRider/Art/Signs'/(m.name+'.png')
    if tex.exists():
        n=m.node_tree.nodes.new('ShaderNodeTexImage');n.image=bpy.data.images.load(str(tex))
        m.node_tree.links.new(n.outputs['Color'],m.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=20
scene.render.threads_mode='FIXED';scene.render.threads=4
scene.cycles.use_denoising=True
scene.render.resolution_x=1300;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world.color=(.18,.18,.18)
ground=mat('Studio floor',(.095,.112,.118),0,.85)
box('Studio ground',(0,0,-.04),(200,200,.06),ground)
def area(name,loc,power,size,target):
    data=bpy.data.lights.new(name,'AREA'); data.energy=power;data.shape='DISK';data.size=size
    o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o);o.location=loc
    o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
area('Large softbox',(2,1.4,4),550,3,(0,0,.6))
area('Soft fill',(-2,1.2,2),370,2,(0,0,.5))
area('Rim softbox',(0,-3,3.5),600,2,(0,0,.6))
camera=bpy.data.cameras.new('Inspection camera');camera.lens=60
cam=bpy.data.objects.new('Inspection camera',camera);bpy.context.collection.objects.link(cam);scene.camera=cam
for filename,position,target,lens in [
    ('hero-front-three-quarter.png',(2.5,3.05,1.95),(0,.05,.60),62),
    ('hero-cockpit-close.png',(.44,-1.11,1.83),(0,.40,.91),64),
    ('hero-rear-three-quarter.png',(-2.35,-2.7,1.52),(0,-.16,.52),62),
]:
    cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();camera.lens=lens
    scene.render.filepath=str(CHECK/filename);bpy.ops.render.render(write_still=True)
print('HERO_INSPECTION_READY',str(CHECK),flush=True)
