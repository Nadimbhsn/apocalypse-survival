"""Étienne, the robot Elron built (Apogée, Gabin Zanetti), modelled in 3D.

A round body of riveted copper plates with a seam belt, an access hatch and a strip of
tape; a big camera-lens eye (housing, glass, glowing iris, shutter ring); an antenna with
a bulb; a three-bladed rotor on a mast; a thruster pod at the back with its flame; a
support cannon under the chin; a crimson scarf round its lower body, its tail streaming.

Animation (one pose function drives everything): a hover loop - bob and sway, rotor
spinning, antenna whipping, scarf fluttering, thruster flickering, the eye breathing - and
a firing pose (cannon recoil, muzzle flash, eye flare).

Run headless:  Blender -b --factory-startup --python etienne3d.py -- OUTDIR [finish ...]
Renders 9 frames (8 hover + 1 fire) per finish into OUTDIR/<finish>_<i>.png, saves
OUTDIR/etienne.blend with a 48-frame keyframed loop.
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector, Euler

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '/tmp/etienne'
WANT = argv[1:] or ['cuivre', 'nuit', 'braise', 'prototype', 'dore']
os.makedirs(OUT, exist_ok=True)

FINISHES = {
    'cuivre': dict(plate=(0.78, 0.47, 0.26), dark=(0.42, 0.22, 0.14), light=(1.0, 0.76, 0.5), eye=(1.0, 0.72, 0.28), scarf=(0.78, 0.12, 0.10)),
    'nuit': dict(plate=(0.30, 0.34, 0.44), dark=(0.14, 0.15, 0.22), light=(0.62, 0.7, 0.86), eye=(0.45, 0.9, 1.0), scarf=(0.32, 0.18, 0.5)),
    'braise': dict(plate=(0.72, 0.16, 0.12), dark=(0.36, 0.06, 0.06), light=(1.0, 0.5, 0.36), eye=(1.0, 0.55, 0.15), scarf=(0.12, 0.08, 0.08)),
    'prototype': dict(plate=(0.88, 0.84, 0.76), dark=(0.5, 0.46, 0.42), light=(1.0, 1.0, 0.95), eye=(0.55, 1.0, 0.55), scarf=(0.85, 0.32, 0.12)),
    'dore': dict(plate=(0.92, 0.72, 0.28), dark=(0.55, 0.36, 0.1), light=(1.0, 0.94, 0.62), eye=(1.0, 0.95, 0.75), scarf=(0.75, 0.1, 0.16)),
}


def srgb_to_lin(c):
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c)


# ---- scene -------------------------------------------------------------------------------------------

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = 512
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
try:
    scene.view_settings.view_transform = 'Standard'
except Exception:
    pass
for attr, val in (('taa_render_samples', 64), ('use_gtao', True), ('gtao_distance', 0.4), ('use_soft_shadows', True), ('shadow_ray_count', 2)):
    try: setattr(scene.eevee, attr, val)
    except Exception: pass
scene.frame_start, scene.frame_end = 1, 48
scene.render.fps = 24

world = bpy.data.worlds.new('Sky')
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (*srgb_to_lin((0.55, 0.45, 0.62)), 1)   # dusky violet ambient
bg.inputs['Strength'].default_value = 0.7

ROOT = bpy.data.objects.new('Etienne', None)
scene.collection.objects.link(ROOT)


def link(obj, parent=ROOT):
    if obj.name not in scene.collection.objects:
        try: scene.collection.objects.link(obj)
        except RuntimeError: pass
    obj.parent = parent
    return obj


def active(obj):
    for o in bpy.context.selected_objects: o.select_set(False)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)


def smooth(obj, sub=1):
    for p in obj.data.polygons: p.use_smooth = True
    if sub:
        m = obj.modifiers.new('Sub', 'SUBSURF')
        m.levels = m.render_levels = sub


def bevel(obj, w=0.02, seg=3):
    m = obj.modifiers.new('Bevel', 'BEVEL')
    m.width = w
    m.segments = seg
    m.limit_method = 'ANGLE'


# ---- materials ---------------------------------------------------------------------------------------

MATS = {}


def principled(name, color, metal=0.0, rough=0.5, emit=None, emit_strength=0.0, alpha=1.0, bump=0.0, coat=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*srgb_to_lin(color), 1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    if 'Coat Weight' in p.inputs: p.inputs['Coat Weight'].default_value = coat
    if emit is not None:
        p.inputs['Emission Color'].default_value = (*srgb_to_lin(emit), 1)
        p.inputs['Emission Strength'].default_value = emit_strength
    if alpha < 1.0:
        p.inputs['Alpha'].default_value = alpha
        try: m.surface_render_method = 'BLENDED'
        except Exception: m.blend_method = 'BLEND'
    if bump > 0:
        # hammered, worn plates: a little noise in the surface
        tex = nt.nodes.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value = 18.0; tex.inputs['Detail'].default_value = 6.0
        b = nt.nodes.new('ShaderNodeBump'); b.inputs['Strength'].default_value = bump; b.inputs['Distance'].default_value = 0.02
        nt.links.new(tex.outputs['Fac'], b.inputs['Height'])
        nt.links.new(b.outputs['Normal'], p.inputs['Normal'])
        # and patina in the colour
        mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
        mix.inputs['Factor'].default_value = 0.35
        ramp = nt.nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].color = (0.75, 0.72, 0.7, 1); ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
        tex2 = nt.nodes.new('ShaderNodeTexNoise'); tex2.inputs['Scale'].default_value = 4.0
        nt.links.new(tex2.outputs['Fac'], ramp.inputs['Fac'])
        mix.inputs['A'].default_value = (*srgb_to_lin(color), 1)
        nt.links.new(ramp.outputs['Color'], mix.inputs['B'])
        nt.links.new(mix.outputs['Result'], p.inputs['Base Color'])
        m['tint_node'] = mix.name
    MATS[name] = m
    return m


def set_color(name, color, emit_strength=None):
    m = MATS[name]
    p = m.node_tree.nodes['Principled BSDF']
    lin = (*srgb_to_lin(color), 1)
    if 'tint_node' in m.keys():
        m.node_tree.nodes[m['tint_node']].inputs['A'].default_value = lin
    else:
        p.inputs['Base Color'].default_value = lin
    if emit_strength is not None:
        p.inputs['Emission Color'].default_value = lin
        p.inputs['Emission Strength'].default_value = emit_strength


principled('Plate', (0.78, 0.47, 0.26), metal=0.7, rough=0.46, bump=0.3)
principled('Dark', (0.42, 0.22, 0.14), metal=0.6, rough=0.5)
principled('HatchPlate', (0.78, 0.47, 0.26), metal=0.4, rough=0.65, bump=0.15)
principled('Brass', (1.0, 0.76, 0.5), metal=0.9, rough=0.28)
principled('Glass', (0.04, 0.03, 0.05), metal=0.0, rough=0.05, coat=1.0)
principled('Iris', (1.0, 0.72, 0.28), emit=(1.0, 0.72, 0.28), emit_strength=4.0)
principled('Pupil', (0.02, 0.02, 0.02), rough=0.2)
principled('Shine', (1, 1, 1), emit=(1, 1, 1), emit_strength=6.0)
principled('Bulb', (1.0, 0.3, 0.2), emit=(1.0, 0.3, 0.2), emit_strength=3.0)
principled('Tape', (0.84, 0.78, 0.62), rough=0.8)
principled('Scarf', (0.78, 0.12, 0.10), rough=0.85)
p_ = MATS['Scarf'].node_tree.nodes['Principled BSDF']
if 'Sheen Weight' in p_.inputs: p_.inputs['Sheen Weight'].default_value = 0.2
principled('Flame', (1.0, 0.5, 0.15), emit=(1.0, 0.45, 0.12), emit_strength=6.0, alpha=0.7)
principled('FlameCore', (1.0, 0.85, 0.5), emit=(1.0, 0.85, 0.5), emit_strength=8.0, alpha=0.9)
principled('Flash', (1.0, 0.9, 0.6), emit=(1.0, 0.9, 0.6), emit_strength=12.0, alpha=0.9)
principled('Blur', (0.35, 0.25, 0.22), rough=0.6, alpha=0.12)
ink = bpy.data.materials.new('Ink')
ink.use_nodes = True
ink.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.02, 0.012, 0.02, 1)
ink.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 1.0
ink.use_backface_culling = True
MATS['Ink'] = ink


def mat(obj, *names):
    obj.data.materials.clear()
    for n in names: obj.data.materials.append(MATS[n])


def outline(obj, thick=0.028):
    """Inverted hull: a slightly larger copy turned inside out, drawn in ink - the painted sprites' line."""
    obj.data.materials.append(MATS['Ink'])
    m = obj.modifiers.new('Outline', 'SOLIDIFY')
    m.thickness = thick
    m.offset = 1.0
    m.use_flip_normals = True
    m.use_rim = False
    m.material_offset = len(obj.data.materials) - 1


# ---- the body ---------------------------------------------------------------------------------------

bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32, radius=1.0)
body = link(bpy.context.object); body.name = 'Body'
body.scale = (1.0, 1.0, 0.95)
mat(body, 'Plate'); smooth(body, 1); outline(body, 0.035)

# underside a shade darker: a second shell cap
bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32, radius=1.006)
under = link(bpy.context.object); under.name = 'BellyPlate'
bm = bmesh.new(); bm.from_mesh(under.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z > -0.28], context='VERTS')
bm.to_mesh(under.data); bm.free()
under.scale = (1, 1, 0.95)
mat(under, 'Dark'); smooth(under, 1)
sol = under.modifiers.new('Thick', 'SOLIDIFY'); sol.thickness = 0.02


def torus(name, major, minor, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), m='Dark', parent=ROOT, seg=96):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=seg, minor_segments=12, location=loc, rotation=rot)
    o = link(bpy.context.object, parent); o.name = name
    o.scale = scale
    mat(o, m); smooth(o, 0)
    return o


# seam belt round the middle and two seams over the top
torus('Belt', 1.0, 0.045, scale=(1, 1, 1))
torus('SeamA', 0.985, 0.03, rot=(math.radians(90), 0, math.radians(20)), scale=(1, 1, 0.96))
torus('SeamB', 0.985, 0.03, rot=(math.radians(90), 0, math.radians(-50)), scale=(1, 1, 0.96))


def on_body(theta, phi, r=1.0):
    """A point on the body: theta round the vertical axis (0 = front, +X), phi up from the equator."""
    return Vector((math.cos(phi) * math.cos(theta) * r, math.cos(phi) * math.sin(theta) * r, math.sin(phi) * r * 0.95))


rivets = []
def rivet(p, r=0.042):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=r, location=p)
    o = link(bpy.context.object); o.name = 'Rivet'
    o.scale = (1, 1, 0.75)
    o.rotation_euler = Vector(p).to_track_quat('Z', 'Y').to_euler()
    mat(o, 'Brass'); smooth(o, 0)
    rivets.append(o)
    return o


for k in range(18):
    th = k * 2 * math.pi / 18
    if min(abs(th), 2 * math.pi - th) < math.radians(38): continue     # hidden behind the eye
    rivet(on_body(th, math.radians(6), 1.01)); rivet(on_body(th, math.radians(-6), 1.01))
for k in range(1, 9):
    ph = -math.pi / 2 + k * math.pi / 9
    for th in (math.radians(20), math.radians(-50)):
        rivet(on_body(th + 0.06, ph, 1.008), 0.033)

# the access hatch on the visible flank (-Y), four rivets at its corners
bpy.ops.mesh.primitive_grid_add(x_subdivisions=10, y_subdivisions=10, size=0.5)
hatch = link(bpy.context.object); hatch.name = 'Hatch'
hatch.location = on_body(math.radians(-100), math.radians(28), 1.0)
hatch.rotation_euler = hatch.location.to_track_quat('Z', 'Y').to_euler()
hatch.scale = (1.0, 0.75, 1)
sw = hatch.modifiers.new('Wrap', 'SHRINKWRAP'); sw.target = body; sw.offset = 0.02
so = hatch.modifiers.new('Thick', 'SOLIDIFY'); so.thickness = 0.03
mat(hatch, 'HatchPlate'); smooth(hatch, 0); bevel(hatch, 0.01, 2)
for dx in (-0.2, 0.2):
    for dz in (-0.14, 0.14):
        th = math.radians(-100) + dx; ph = math.radians(28) + dz
        rivet(on_body(th, ph, 1.04), 0.028)

# a strip of tape: home-made
bpy.ops.mesh.primitive_grid_add(x_subdivisions=12, y_subdivisions=3, size=1.0)
tape = link(bpy.context.object); tape.name = 'Tape'
tape.scale = (0.42, 0.11, 1)
tape.location = on_body(math.radians(-150), math.radians(50), 1.0)
tape.rotation_euler = tape.location.to_track_quat('Z', 'Y').to_euler()
tape.rotation_euler.z += 0.6
sw = tape.modifiers.new('Wrap', 'SHRINKWRAP'); sw.target = body; sw.offset = 0.012
so = tape.modifiers.new('Thick', 'SOLIDIFY'); so.thickness = 0.008
mat(tape, 'Tape')

# a speaker grille under the eye, on the flank
for i in range(4):
    for j in range(3):
        th = math.radians(-58) + i * 0.09; ph = math.radians(-30) + j * 0.09
        bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.022, depth=0.04, location=on_body(th, ph, 1.0))
        o = link(bpy.context.object); o.name = 'Grille'
        o.rotation_euler = o.location.to_track_quat('Z', 'Y').to_euler()
        mat(o, 'Pupil')

# ---- the eye ----------------------------------------------------------------------------------------

EYE = bpy.data.objects.new('EyeRig', None); link(EYE)
EYE.location = (0.87, 0.0, -0.06)
EYE.rotation_euler = (0, math.radians(90), 0)       # local +Z looks along +X
bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.5, depth=0.42)
housing = link(bpy.context.object, EYE); housing.name = 'EyeHousing'
housing.location = (0, 0, 0.0)
mat(housing, 'Dark'); smooth(housing, 0); bevel(housing, 0.05, 4); outline(housing, 0.03)
torus('EyeRim', 0.47, 0.05, loc=(0, 0, 0.21), m='Brass', parent=EYE, seg=64)
torus('Shutter', 0.39, 0.04, loc=(0, 0, 0.23), m='Dark', parent=EYE, seg=64)
for k in range(12):                       # shutter ridges
    a = k * 2 * math.pi / 12
    bpy.ops.mesh.primitive_cube_add(size=1, location=(math.cos(a) * 0.42, math.sin(a) * 0.42, 0.22))
    o = link(bpy.context.object, EYE); o.name = 'Ridge'
    o.scale = (0.06, 0.02, 0.03); o.rotation_euler = (0, 0, a)
    o.location = (math.cos(a) * 0.42, math.sin(a) * 0.42, 0.22)
    mat(o, 'Brass')
bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=0.36)
lens = link(bpy.context.object, EYE); lens.name = 'Lens'
lens.location = (0, 0, 0.16); lens.scale = (1, 1, 0.35)
mat(lens, 'Glass'); smooth(lens, 1)
bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.3, depth=0.02)
iris = link(bpy.context.object, EYE); iris.name = 'Iris'
iris.location = (0, 0, 0.255)
mat(iris, 'Iris')
bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.11, depth=0.02)
pupil = link(bpy.context.object, EYE); pupil.name = 'Pupil'
pupil.location = (0, 0, 0.268)
mat(pupil, 'Pupil')
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.07)
glint = link(bpy.context.object, EYE); glint.name = 'Glint'
glint.location = (0.13, -0.12, 0.29); glint.scale = (1, 1, 0.4)
mat(glint, 'Shine')
EYE_LIGHT = bpy.data.lights.new('EyeGlow', 'POINT'); EYE_LIGHT.energy = 25; EYE_LIGHT.shadow_soft_size = 0.3
eye_light = bpy.data.objects.new('EyeGlow', EYE_LIGHT); link(eye_light, EYE); eye_light.location = (0, 0, 0.55)

# ---- the antenna --------------------------------------------------------------------------------------

ANT = bpy.data.objects.new('AntennaRig', None); link(ANT)
ANT.location = on_body(math.radians(180), math.radians(68), 0.98)
curve = bpy.data.curves.new('AntennaCurve', 'CURVE'); curve.dimensions = '3D'
curve.bevel_depth = 0.028; curve.bevel_resolution = 4
sp = curve.splines.new('BEZIER'); sp.bezier_points.add(1)
p0, p1 = sp.bezier_points
p0.co = (0, 0, 0); p0.handle_left = (0, 0, -0.1); p0.handle_right = (0.05, 0, 0.3)
p1.co = (-0.32, 0, 0.78); p1.handle_left = (-0.15, 0, 0.6); p1.handle_right = (-0.4, 0, 0.9)
ant = bpy.data.objects.new('Antenna', curve); link(ant, ANT)
ant.data.materials.append(MATS['Dark'])
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.07, depth=0.08, location=(0, 0, 0))
base = link(bpy.context.object, ANT); base.name = 'AntennaBase'; mat(base, 'Brass')
TIP = bpy.data.objects.new('Tip', None); link(TIP, ANT); TIP.location = (-0.32, 0, 0.8)
bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.1)
bulb = link(bpy.context.object, TIP); bulb.name = 'Bulb'; bulb.location = (0, 0, 0)
mat(bulb, 'Bulb'); smooth(bulb, 0); outline(bulb, 0.02)
torus('BulbCollar', 0.07, 0.025, loc=(0, 0, -0.09), m='Brass', parent=TIP, seg=24)
BULB_LIGHT = bpy.data.lights.new('BulbGlow', 'POINT'); BULB_LIGHT.energy = 6; BULB_LIGHT.color = (1, 0.35, 0.25)
bl = bpy.data.objects.new('BulbGlow', BULB_LIGHT); link(bl, TIP)

# ---- the rotor ----------------------------------------------------------------------------------------

top = on_body(0, math.radians(90), 0.95)
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.055, depth=0.42, location=(0, 0, top.z + 0.16))
mast = link(bpy.context.object); mast.name = 'Mast'; mat(mast, 'Dark'); smooth(mast, 0)
torus('MastCollar', 0.12, 0.04, loc=(0, 0, top.z - 0.01), m='Brass')
ROTOR = bpy.data.objects.new('Rotor', None); link(ROTOR); ROTOR.location = (0, 0, top.z + 0.36)
bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.13, depth=0.09, location=(0, 0, 0))
hub = link(bpy.context.object, ROTOR); hub.name = 'Hub'; mat(hub, 'Brass'); bevel(hub, 0.02, 2); outline(hub, 0.02)
for k in range(3):
    a = k * 2 * math.pi / 3
    bpy.ops.mesh.primitive_cube_add(size=1)
    bl_ = link(bpy.context.object, ROTOR); bl_.name = 'Blade'
    bl_.scale = (0.78, 0.13, 0.022)
    bl_.location = (math.cos(a) * 0.48, math.sin(a) * 0.48, 0)
    bl_.rotation_euler = (math.radians(12), 0, a)
    mat(bl_, 'Dark'); bevel(bl_, 0.02, 2); outline(bl_, 0.015)
bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.9, depth=0.01, location=(0, 0, 0))
blur = link(bpy.context.object, ROTOR); blur.name = 'RotorBlur'; mat(blur, 'Blur')

# ---- the thruster ---------------------------------------------------------------------------------------

THR = bpy.data.objects.new('Thruster', None); link(THR)
THR.location = (-0.92, 0, 0.02); THR.rotation_euler = (0, math.radians(-90), 0)   # local +Z points to -X
bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.27, depth=0.45, location=(0, 0, 0.12))
pod = link(bpy.context.object, THR); pod.name = 'Pod'; mat(pod, 'Dark'); bevel(pod, 0.04, 3); smooth(pod, 0); outline(pod, 0.03)
torus('PodRing', 0.27, 0.035, loc=(0, 0, 0.0), m='Brass', parent=THR, seg=48)
bpy.ops.mesh.primitive_cone_add(vertices=32, radius1=0.2, radius2=0.27, depth=0.2, location=(0, 0, 0.42))
noz = link(bpy.context.object, THR); noz.name = 'Nozzle'; mat(noz, 'Brass'); smooth(noz, 0)
bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.18, depth=0.02, location=(0, 0, 0.52))
core = link(bpy.context.object, THR); core.name = 'Core'; mat(core, 'FlameCore')
bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=0.17, radius2=0.0, depth=0.6, location=(0, 0, 0.82))
flame = link(bpy.context.object, THR); flame.name = 'Flame'; mat(flame, 'Flame'); smooth(flame, 0)
bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=0.09, radius2=0.0, depth=0.32, location=(0, 0, 0.66))
inner = link(bpy.context.object, THR); inner.name = 'FlameCore'; mat(inner, 'FlameCore'); smooth(inner, 0)
THR_LIGHT = bpy.data.lights.new('ThrusterGlow', 'POINT'); THR_LIGHT.energy = 40; THR_LIGHT.color = (1, 0.55, 0.2)
tl = bpy.data.objects.new('ThrusterGlow', THR_LIGHT); link(tl, THR); tl.location = (0, 0, 0.9)

# ---- the cannon -----------------------------------------------------------------------------------------

GUN = bpy.data.objects.new('Cannon', None); link(GUN)
GUN.location = (0.42, 0, -0.78)
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.08))
mount = link(bpy.context.object, GUN); mount.name = 'Mount'; mount.scale = (0.32, 0.2, 0.2); mat(mount, 'Dark'); bevel(mount, 0.03, 3); outline(mount, 0.02)
BARREL = bpy.data.objects.new('Barrel', None); link(BARREL, GUN)
bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.085, depth=0.62, location=(0.32, 0, 0), rotation=(0, math.radians(90), 0))
barrel = link(bpy.context.object, BARREL); barrel.name = 'BarrelTube'; mat(barrel, 'Dark'); smooth(barrel, 0); outline(barrel, 0.018)
torus('Brake', 0.1, 0.035, loc=(0.6, 0, 0), rot=(0, math.radians(90), 0), m='Brass', parent=BARREL, seg=24)
torus('BarrelBand', 0.095, 0.025, loc=(0.2, 0, 0), rot=(0, math.radians(90), 0), m='Brass', parent=BARREL, seg=24)
flash = bpy.data.objects.new('MuzzleFlash', None); link(flash, GUN); flash.location = (0.72, 0, 0)
principled('FlashOuter', (1.0, 0.55, 0.15), emit=(1.0, 0.5, 0.12), emit_strength=10.0, alpha=0.75)
for k, (rx, ry, ln, rad, m_) in enumerate(((0, 0, 0.55, 0.16, 'FlashOuter'), (0, 0, 0.38, 0.09, 'Flash'), (0.6, 0, 0.3, 0.07, 'FlashOuter'), (-0.6, 0, 0.3, 0.07, 'FlashOuter'), (0, 0.6, 0.28, 0.06, 'FlashOuter'), (0, -0.6, 0.28, 0.06, 'FlashOuter'))):
    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=rad, radius2=0.0, depth=ln)
    c = link(bpy.context.object, flash); c.name = 'FlashCone'
    c.rotation_euler = Euler((ry, math.radians(90) + rx, 0))
    c.location = Vector((ln / 2, 0, 0))
    mat(c, m_); smooth(c, 0)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.12, location=(0, 0, 0))
fb = link(bpy.context.object, flash); fb.name = 'FlashBall'; mat(fb, 'Flash')
FLASH_LIGHT = bpy.data.lights.new('FlashLight', 'POINT'); FLASH_LIGHT.energy = 0; FLASH_LIGHT.color = (1, 0.85, 0.5)
fl = bpy.data.objects.new('FlashLight', FLASH_LIGHT); link(fl, GUN); fl.location = (0.9, -0.2, 0)

# ---- the scarf --------------------------------------------------------------------------------------------

# the band round the lower body
bpy.ops.mesh.primitive_torus_add(major_radius=0.86, minor_radius=0.09, major_segments=64, minor_segments=12, location=(0, 0, -0.42))
band = link(bpy.context.object); band.name = 'ScarfBand'
band.scale = (1.0, 1.0, 1.7)
mat(band, 'Scarf'); smooth(band, 1); outline(band, 0.02)
# its knot at the back
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=0.14, location=(-0.84, -0.12, -0.45))
knot = link(bpy.context.object); knot.name = 'Knot'; knot.scale = (0.8, 1, 1.1); mat(knot, 'Scarf'); smooth(knot, 1); outline(knot, 0.02)
# the tail: a strip of cloth, deformed for each pose
TAIL_N = 24
bpy.ops.mesh.primitive_grid_add(x_subdivisions=TAIL_N, y_subdivisions=3, size=1.0)
tail = link(bpy.context.object); tail.name = 'ScarfTail'
mat(tail, 'Scarf')
so = tail.modifiers.new('Thick', 'SOLIDIFY'); so.thickness = 0.03
smooth(tail, 1)
outline(tail, 0.015)
tail_base = [v.co.copy() for v in tail.data.vertices]   # grid in [-0.5, 0.5]^2


def shape_tail(t, firing=False):
    """The tail streams back from the knot, waving along its length; t in [0, 1) round the loop."""
    L, Wd = 2.05, 0.34
    ph = t * 2 * math.pi
    for v, b in zip(tail.data.vertices, tail_base):
        u = b.x + 0.5            # 0 at the knot, 1 at the free end
        w = b.y * Wd * (1 - 0.45 * u)
        x = -0.86 - u * L
        z = -0.5 - u * 0.3 + w + math.sin(ph * 2 - u * 5.0) * 0.24 * u ** 1.2
        y = -0.12 + math.sin(ph * 2 - u * 4.0 + 1.0) * 0.12 * u + w * 0.25
        if firing: z += 0.08 * u
        v.co = (x, y, z)
    tail.data.update()


# ---- lights and camera -------------------------------------------------------------------------------------

def sun(name, energy, color, rot):
    l = bpy.data.lights.new(name, 'SUN'); l.energy = energy; l.color = color; l.angle = math.radians(8)
    o = bpy.data.objects.new(name, l); scene.collection.objects.link(o); o.rotation_euler = rot
    return o

sun('Key', 3.2, (1.0, 0.86, 0.72), (math.radians(50), 0, math.radians(-35)))       # sunset key, upper left front
sun('Rim', 5.0, (1.0, 0.6, 0.35), (math.radians(-60), 0, math.radians(150)))       # warm rim from behind
sun('Fill', 0.6, (0.6, 0.65, 1.0), (math.radians(110), 0, math.radians(20)))        # cool fill from below

cam_data = bpy.data.cameras.new('Cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 4.7
cam = bpy.data.objects.new('Cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
# a three-quarter view: he faces right (+X), a little toward us, seen a little from above
cam.location = (3.2, -9.0, 2.2)
direction = Vector((0, 0, 0)) - cam.location
cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
cam_data.shift_x = 0.02
cam_data.shift_y = 0.06

# ---- the pose ----------------------------------------------------------------------------------------------

def pose(t, firing=False):
    ph = t * 2 * math.pi
    ROOT.location = (0, 0, math.sin(ph) * 0.06)
    ROOT.rotation_euler = (math.sin(ph) * 0.03, math.sin(ph + 1.0) * 0.05 + (0.06 if firing else 0), math.radians(-25))
    ROTOR.rotation_euler = (0, 0, t * 2 * math.pi / 3)          # 120 degrees a loop: the three blades come round
    ANT.rotation_euler = (0, math.sin(ph * 2 + 0.6) * 0.16 - (0.15 if firing else 0), math.sin(ph + 0.3) * 0.08)
    blink = 0.85 + 0.15 * abs(math.sin(ph * 2))
    MATS['Bulb'].node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 2.0 + 3.0 * blink
    eye_e = 9.0 if firing else 5.5 + 0.8 * math.sin(ph)
    MATS['Iris'].node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = eye_e
    EYE_LIGHT.energy = 60 if firing else 25
    fl_k = 0.85 + 0.25 * abs(math.sin(ph * 5)) + 0.1 * math.sin(ph * 11)
    flame.scale = (1.0, 1.0, fl_k)
    flame.location = (0, 0, 0.52 + 0.3 * fl_k)
    THR_LIGHT.energy = 30 + 20 * fl_k
    BARREL.location = (-0.12 if firing else 0.0, 0, 0)
    for c in flash.children: c.hide_render = not firing
    FLASH_LIGHT.energy = 300 if firing else 0
    shape_tail(t, firing)


def apply_finish(f):
    F = FINISHES[f]
    set_color('Plate', F['plate'])
    set_color('HatchPlate', tuple(c * 0.92 for c in F['plate']))
    set_color('Dark', F['dark'])
    set_color('Brass', F['light'])
    set_color('Iris', F['eye'], 4.0)
    set_color('Scarf', tuple(c * 0.82 for c in F['scarf']))
    EYE_LIGHT.color = F['eye']


# ---- render the sprite frames ----------------------------------------------------------------------------

GLOWING = {'Iris', 'Shine', 'Bulb', 'Flame', 'FlameCore', 'Flash', 'FlashOuter'}
HOLD = bpy.data.materials.new('Hold')
HOLD.use_nodes = True
_nt = HOLD.node_tree
for n in list(_nt.nodes): _nt.nodes.remove(n)
_out = _nt.nodes.new('ShaderNodeOutputMaterial'); _ho = _nt.nodes.new('ShaderNodeHoldout')
_nt.links.new(_ho.outputs[0], _out.inputs['Surface'])

for f in [w for w in WANT if w in FINISHES]:
    apply_finish(f)
    for i in range(9):
        firing = i == 8
        pose(0.1 if firing else i / 8, firing)
        scene.render.filepath = os.path.join(OUT, f'{f}_{i}.png')
        bpy.ops.render.render(write_still=True)
        # the same frame with only what shines visible (the rest holds it out): the halo's source
        swapped = []
        for o in scene.objects:
            if o.type != 'MESH' and o.type != 'CURVE': continue
            for si, slot in enumerate(o.material_slots):
                if slot.material is not None and slot.material.name not in GLOWING:
                    swapped.append((o, si, slot.material)); slot.material = HOLD
        scene.render.filepath = os.path.join(OUT, f'{f}_{i}_glow.png')
        bpy.ops.render.render(write_still=True)
        for o, si, m_ in swapped: o.material_slots[si].material = m_
        print('RENDERED', f, i, flush=True)

# ---- keep an animated .blend: a 48-frame loop with keyframes ---------------------------------------------

apply_finish('cuivre')
# the scarf: one shape key per eighth of the loop, crossfaded
tail.shape_key_add(name='Basis')
keys = []
for i in range(8):
    shape_tail(i / 8)
    k = tail.shape_key_add(name=f'Wave{i}', from_mix=False)
    for v, sk in zip(tail.data.vertices, k.data): sk.co = v.co.copy()
    keys.append(k)
shape_tail(0)
for frame in range(1, 49):
    t = (frame - 1) / 48
    pose(t)
    ROTOR.rotation_euler = (0, 0, t * 7 * 2 * math.pi / 3)   # seven thirds of a turn a loop: a lively spin, still seamless
    for obj, prop in ((ROOT, 'location'), (ROOT, 'rotation_euler'), (ROTOR, 'rotation_euler'), (ANT, 'rotation_euler'), (flame, 'scale'), (flame, 'location')):
        obj.keyframe_insert(prop, frame=frame)
    seg = t * 8
    for i, k in enumerate(keys):
        d = min(abs(seg - i), 8 - abs(seg - i))
        k.value = max(0.0, 1.0 - d)
        k.keyframe_insert('value', frame=frame)
# the rotor keeps turning: make its curve linear
for fc in (ROTOR.animation_data.action.fcurves if ROTOR.animation_data and hasattr(ROTOR.animation_data.action, 'fcurves') else []):
    for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'etienne.blend'))
print('SAVED_BLEND', flush=True)
