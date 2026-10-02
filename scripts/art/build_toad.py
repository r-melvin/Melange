"""The psychedelics spoke's toad: a squat Colorado River toad (Incilius alvarius) in a static sitting pose: a broad, flat
olive-brown body with a pale belly, a wide head with gold eyes, the big kidney-shaped parotoid glands behind the eyes (the
venom glands the player milks), the pale tubercles at the corners of the jaw, folded hind legs with large glands on them,
front legs propped under the chest, and a few warts on the back.

  Size: about 0.17 m long (snout to rump) and 0.19 m across the splayed hind feet, 0.08 m high. Footprint: well under one cell (a
  terrarium item, not a grid item). Origin: under the toad, centred, on the surface its feet stand on; the toad faces +Y.

Run:  blender -b --python scripts/art/build_toad.py -- [--out DIR] [--render]
Writes toad.glb, toad.mesh.json (one part per colour, Unity axes: x right, y up, z forward; left-handed winding) and toad.png
with --render.

Conventions: metres; Blender Z up, +Y is forward. Same pipeline as build_gunrack.py (primitives, join, export); colours are
lighter than they look in Blender because in-game lighting is darker.
"""
import bmesh
from mathutils import Vector
import bpy
import json
import math
import os
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
RENDER = "--render" in argv
ONLY = argv[argv.index("--only") + 1] if "--only" in argv else None
os.makedirs(OUT, exist_ok=True)

COLORS = {
    "skin": (0.58, 0.54, 0.32),        # olive-brown back and legs
    "gland": (0.72, 0.64, 0.38),       # parotoid and leg glands, a little paler and yellower
    "wart": (0.46, 0.40, 0.25),        # darker warts on the back
    "belly": (0.93, 0.89, 0.76),       # pale belly, jaw tubercles
    "eye": (0.88, 0.72, 0.32),         # gold iris
    "pupil": (0.05, 0.05, 0.05),
}
METAL = ()

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()


_mats = {}


def mat(key, rough=0.6, metal=0.0):
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new(key)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    r, g, b = COLORS[key]
    bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    _mats[key] = m
    return m


def box(name, size, loc, m, bevel=0.0, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        mod = o.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    o.data.materials.append(m)
    return o


def cyl(name, r, h, loc, m, rot=(0, 0, 0), verts=12):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=h, location=loc, rotation=rot, vertices=verts)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(m)
    return o


def cone(name, r1, r2, h, loc, m, rot=(0, 0, 0), verts=12):
    """A truncated cone, r1 at the bottom and r2 at the top."""
    bpy.ops.mesh.primitive_cone_add(radius1=r1, radius2=r2, depth=h, location=loc, rotation=rot, vertices=verts)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(m)
    return o


def rod(name, p0, p1, r, m, verts=8):
    """A cylinder from p0 to p1."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    o = cyl(name, r, d.length, (a + b) / 2, m, verts=verts)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    return o


def label(name, text, size, loc, m, rot=(math.pi / 2, 0, math.pi)):
    """Flat extruded text; the default rotation faces it +Y (readable from the front)."""
    bpy.ops.object.text_add(location=(0, 0, 0))
    o = bpy.context.active_object
    o.data.body = text
    o.data.size = size
    o.data.extrude = 0.002
    o.data.resolution_u = 2
    o.data.align_x = "CENTER"
    o.data.align_y = "CENTER"
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.active_object
    o.name = name
    o.data.materials.clear()
    o.data.materials.append(m)
    o.rotation_euler = rot
    o.location = loc
    return o



def ball(name, r, loc, m, scale=(1, 1, 1), seg=10, rings=6, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=seg, ring_count=rings, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(m)
    return o


def blob(name, p0, p1, r, m, flat=1.0, seg=7, rings=4):
    """An ellipsoid spanning p0 to p1, radius r across (times flat in the up-ish direction): a limb segment."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, location=(a + b) / 2, segments=seg, ring_count=rings)
    o = bpy.context.active_object
    o.name = name
    o.scale = (r, r * flat, d.length / 2)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    o.data.materials.append(m)
    return o


def join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)     # mesh coordinates = model space
    return obj


def build():
    reset()
    skin, gland, wart, belly, eye, pupil = mat("skin", 0.7), mat("gland", 0.6), mat("wart", 0.8), mat("belly", 0.8), mat("eye", 0.3), mat("pupil", 0.2)
    parts = []
    # body and belly: a broad, flat ellipsoid, the pale belly bulging just below it
    parts.append(ball("body", 1.0, (0, -0.012, 0.040), skin, scale=(0.054, 0.072, 0.033), seg=12, rings=8, rot=(0.14, 0, 0)))
    parts.append(ball("belly", 1.0, (0, -0.004, 0.024), belly, scale=(0.047, 0.064, 0.021), seg=12, rings=6))
    # the head: wide and flat, the snout blunt
    parts.append(ball("head", 1.0, (0, 0.052, 0.044), skin, scale=(0.044, 0.038, 0.025), seg=10, rings=6, rot=(0.10, 0, 0)))
    parts.append(ball("jaw", 1.0, (0, 0.056, 0.031), belly, scale=(0.037, 0.030, 0.012), seg=10, rings=5))
    # eyes on top of the head, gold with a dark horizontal pupil
    for sx in (-1, 1):
        parts.append(ball(f"eye_{sx}", 0.0115, (sx * 0.027, 0.062, 0.062), eye, seg=8, rings=6))
        parts.append(ball(f"pupil_{sx}", 0.006, (sx * 0.0335, 0.0675, 0.0665), pupil, scale=(1.0, 1.0, 0.55), seg=8, rings=4))
        # the parotoid glands: big, kidney-shaped, sloping back and out from behind each eye
        parts.append(blob(f"parotoid_{sx}", (sx * 0.026, 0.046, 0.066), (sx * 0.036, 0.004, 0.062), 0.0125, gland, flat=0.8))
        # the pale tubercle at the corner of the jaw
        parts.append(ball(f"tubercle_{sx}", 0.006, (sx * 0.040, 0.040, 0.034), belly, seg=6, rings=4))
    # warts scattered on the back, dropped onto the body's surface
    body = parts[0]
    for k, (x, y) in enumerate(((-0.018, -0.010), (0.016, -0.018), (-0.030, -0.040), (0.028, -0.045), (0.002, -0.050),
                                (-0.010, 0.010), (0.034, -0.008), (-0.036, -0.016))):
        hit, loc, _, _ = body.ray_cast(Vector((x, y, 0.2)) - body.location, Vector((0, 0, -1)))
        z = (loc + body.location).z if hit else 0.06
        parts.append(ball(f"wart_{k}", 0.0045, (x, y, z), wart, scale=(1, 1, 0.6), seg=5, rings=3))
    for sx in (-1, 1):
        # front legs: one tapered limb from the shoulder down and forward to a splayed hand
        parts.append(blob(f"arm_{sx}", (sx * 0.035, 0.032, 0.032), (sx * 0.046, 0.058, 0.002), 0.0095, skin))
        hand = (sx * 0.046, 0.060, 0.003)
        parts.append(ball(f"hand_{sx}", 0.010, hand, skin, scale=(1.0, 0.9, 0.3), seg=7, rings=4))
        for t in (-1, 0, 1):
            a = math.radians(90 - sx * 30 + t * 30)                     # fingers splay forward and a little out
            tip = (hand[0] + 0.017 * math.cos(a), hand[1] + 0.017 * math.sin(a), 0.003)
            parts.append(blob(f"finger_{sx}_{t}", hand, tip, 0.0032, skin, flat=0.7, seg=5, rings=3))
        # hind legs folded along the flanks: thigh forward, shank back, a long foot forward and out
        hip, knee, ankle = (sx * 0.036, -0.060, 0.024), (sx * 0.064, -0.016, 0.017), (sx * 0.058, -0.068, 0.009)
        parts.append(blob(f"thigh_{sx}", hip, knee, 0.019, skin, flat=0.8))
        parts.append(blob(f"shank_{sx}", knee, ankle, 0.012, skin, flat=0.8))
        parts.append(blob(f"leg_gland_{sx}", (sx * 0.068, -0.026, 0.024), (sx * 0.064, -0.054, 0.018), 0.0065, gland))
        foot0, foot1 = (sx * 0.060, -0.068, 0.004), (sx * 0.080, -0.036, 0.003)
        parts.append(blob(f"foot_{sx}", foot0, foot1, 0.010, skin, flat=0.35))
        for t in (-1, 0, 1):
            a = math.atan2(foot1[1] - foot0[1], foot1[0] - foot0[0]) + math.radians(22 * t)
            tip = (foot1[0] + 0.020 * math.cos(a), foot1[1] + 0.020 * math.sin(a), 0.003)
            parts.append(blob(f"toe_{sx}_{t}", foot1, tip, 0.0032, skin, flat=0.7, seg=5, rings=3))
    obj = join(parts, "toad")
    # sit the lowest point on the origin's plane
    zmin = min(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:
        v.co.z -= zmin
    return obj


def export_glb(obj, name):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, f"{name}.glb"), use_selection=True, export_format="GLB")


def export_json(obj, name):
    """One part per material colour, triangulated; Blender (x, y, z) -> Unity (x, z, y). That axis swap mirrors, so each triangle's
    winding is reversed to keep the faces pointing outwards in Unity's left-handed space."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    parts = {}
    lo, hi = [1e9] * 3, [-1e9] * 3
    for face in bm.faces:
        key = obj.data.materials[face.material_index].name
        part = parts.setdefault(key, {"color": list(COLORS[key]), "metal": key in METAL, "v": [], "n": [], "t": []})
        base = len(part["v"]) // 3
        for loop in face.loops:
            co = loop.vert.co
            part["v"] += [round(co.x, 5), round(co.z, 5), round(co.y, 5)]
            nn = face.normal if not face.smooth else loop.vert.normal
            part["n"] += [round(nn.x, 4), round(nn.z, 4), round(nn.y, 4)]
            for k, c in enumerate((co.x, co.z, co.y)):
                lo[k], hi[k] = min(lo[k], c), max(hi[k], c)
        part["t"] += [base, base + 2, base + 1]
    bm.free()
    size = [round(hi[k] - lo[k], 3) for k in range(3)]
    data = {"name": name, "size": size, "parts": list(parts.values())}
    with open(os.path.join(OUT, f"{name}.mesh.json"), "w") as f:
        json.dump(data, f, separators=(",", ":"))
    tris = sum(len(p["t"]) // 3 for p in parts.values())
    print(f"{name}: {len(parts)} parts, {tris} triangles, size {size}")


def render_preview(obj, name, reach=2.4, view=(0.55, 1.0, 0.6), floor=0.0):
    """A 3/4 view from the front right; reach is the camera distance in multiples of the model's largest dimension."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "CYCLES"
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.55, 0.58, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    scene.world = world
    ground = bpy.data.materials.new("ground")
    ground.use_nodes = True
    ground.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.45, 0.44, 0.42, 1)
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, floor))
    bpy.context.active_object.data.materials.append(ground)
    bpy.ops.object.light_add(type="SUN", location=(3, 4, 6))
    sun = bpy.context.active_object
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(150))
    dims = obj.dimensions
    target = Vector((0, 0, floor + dims.z * 0.5))
    cam_loc = target + Vector(view).normalized() * max(dims.x, dims.y, dims.z) * reach
    bpy.ops.object.camera_add(location=cam_loc)
    cam = bpy.context.active_object
    cam.rotation_euler = (target - cam_loc).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = os.path.join(OUT, f"{name}.png")
    bpy.ops.render.render(write_still=True)


MODELS = {"toad": (build, dict(reach=2.4, view=(0.7, 1.0, 0.8)))}
for model, (fn, view) in MODELS.items():
    if ONLY and model != ONLY:
        continue
    o = fn()
    export_glb(o, model)
    export_json(o, model)
    if RENDER:
        render_preview(o, model, **view)
