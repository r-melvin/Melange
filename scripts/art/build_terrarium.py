"""The psychedelics spoke's terrarium (Randy's Bait & Tackle sells it; it houses toads): a glass-walled tank on a wooden stand
cabinet, with a black frame, a mesh lid (a frame with bars, so the inside shows from above), a sand floor, rocks, a flat
basking stone, a log and a water dish. The glass is a light tinted opaque colour (its own part, "glass", so the mod can make
it translucent at runtime if it wants).

  Footprint: 2 x 1 cells (0.96 x 0.48 m). Origin: bottom centre, on the floor. The tank floor (the sand top, where toads
  sit) is 0.83 m up; the interior is about 0.86 x 0.41 m.

Run:  blender -b --python scripts/art/build_terrarium.py -- [--out DIR] [--render]
Writes terrarium.glb, terrarium.mesh.json (one part per colour, Unity axes: x right, y up, z towards the front; left-handed
winding) and terrarium.png with --render.

Conventions: metres; Blender Z up, +Y is the front. Same pipeline as build_gunrack.py (primitives, join, export); colours are
lighter than they look in Blender because in-game lighting is darker.
"""
import bmesh
from mathutils import Vector
import bpy
import json
import math
import os
import random
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
RENDER = "--render" in argv
ONLY = argv[argv.index("--only") + 1] if "--only" in argv else None
os.makedirs(OUT, exist_ok=True)

COLORS = {
    "wood": (0.55, 0.38, 0.24),        # the stand
    "wood_dark": (0.36, 0.24, 0.15),   # stand top, plinth, door lines
    "frame": (0.18, 0.18, 0.20),       # the tank frame and lid
    "glass": (0.74, 0.88, 0.86),       # light tinted glass, opaque
    "sand": (0.86, 0.72, 0.50),
    "rock": (0.64, 0.62, 0.58),
    "slate": (0.48, 0.48, 0.50),
    "log": (0.50, 0.36, 0.24),
    "dish": (0.70, 0.68, 0.62),
    "water": (0.42, 0.66, 0.80),
    "knob": (0.80, 0.70, 0.40),
}
METAL = ("knob",)

W, D = 0.96, 0.48                       # footprint
STAND_H = 0.70
TW, TD, TH = 0.92, 0.45, 0.42          # the tank

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



def ball(name, r, loc, m, scale=(1, 1, 1), sub=1, rot=(0, 0, 0)):
    """A low ico sphere, scaled: rocks."""
    bpy.ops.mesh.primitive_ico_sphere_add(radius=r, location=loc, subdivisions=sub, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(scale=True)
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
    random.seed(7)
    wood, wood_dark, frame, glass = mat("wood", 0.7), mat("wood_dark", 0.7), mat("frame", 0.5), mat("glass", 0.1)
    sand, rock, slate, log, dish, water, knob = (mat("sand", 0.95), mat("rock", 0.9), mat("slate", 0.8), mat("log", 0.9),
                                                 mat("dish", 0.6), mat("water", 0.1), mat("knob", 0.3, 0.8))
    parts = []
    # the stand: a cabinet with a plinth, a top board and two doors
    parts.append(box("plinth", (W - 0.04, D - 0.04, 0.06), (0, 0, 0.03), wood_dark))
    parts.append(box("cabinet", (W - 0.02, D - 0.02, STAND_H - 0.09), (0, 0, 0.06 + (STAND_H - 0.09) / 2), wood, 0.006))
    parts.append(box("stand_top", (W, D, 0.03), (0, 0, STAND_H - 0.015), wood_dark, 0.005))
    for sx in (-1, 1):
        parts.append(box(f"door_{sx}", (W / 2 - 0.06, 0.01, STAND_H - 0.20), (sx * (W / 4 - 0.005), D / 2 - 0.005, 0.36), wood, 0.004))
        parts.append(ball(f"knob_{sx}", 0.014, (sx * 0.05, D / 2 + 0.006, 0.42), knob))
    # the tank: glass panels inside a black frame
    z0 = STAND_H
    g = 0.006
    parts.append(box("tank_floor", (TW, TD, 0.02), (0, 0, z0 + 0.01), frame))
    parts.append(box("glass_front", (TW - 0.02, g, TH), (0, TD / 2 - g / 2, z0 + TH / 2), glass))
    parts.append(box("glass_back", (TW - 0.02, g, TH), (0, -TD / 2 + g / 2, z0 + TH / 2), glass))
    for sx in (-1, 1):
        parts.append(box(f"glass_side_{sx}", (g, TD - 0.02, TH), (sx * (TW / 2 - g / 2), 0, z0 + TH / 2), glass))
    fw = 0.022
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(box(f"post_{sx}_{sy}", (fw, fw, TH), (sx * (TW / 2 - fw / 2 + 0.002), sy * (TD / 2 - fw / 2 + 0.002), z0 + TH / 2), frame))
    for z in (z0 + 0.03, z0 + TH - fw / 2):
        h = 0.06 if z < z0 + 0.1 else fw
        zz = z0 + h / 2 if z < z0 + 0.1 else z
        parts.append(box(f"rail_f_{z:.2f}", (TW + 0.004, fw, h), (0, TD / 2 - fw / 2 + 0.003, zz), frame))
        parts.append(box(f"rail_b_{z:.2f}", (TW + 0.004, fw, h), (0, -TD / 2 + fw / 2 - 0.003, zz), frame))
        for sx in (-1, 1):
            parts.append(box(f"rail_s_{sx}_{z:.2f}", (fw, TD + 0.004, h), (sx * (TW / 2 - fw / 2 + 0.003), 0, zz), frame))
    # the mesh lid: a frame with bars across, sitting on the top rails, and a handle
    lz = z0 + TH + 0.01
    parts.append(box("lid_f", (TW + 0.01, 0.03, 0.02), (0, TD / 2 - 0.015, lz), frame))
    parts.append(box("lid_b", (TW + 0.01, 0.03, 0.02), (0, -TD / 2 + 0.015, lz), frame))
    for sx in (-1, 1):
        parts.append(box(f"lid_s_{sx}", (0.03, TD, 0.02), (sx * (TW / 2 - 0.01), 0, lz), frame))
    nb = 9
    for k in range(nb):
        x = -TW / 2 + 0.03 + (TW - 0.06) * (k + 1) / (nb + 1)
        parts.append(box(f"bar_{k}", (0.008, TD - 0.04, 0.008), (x, 0, lz + 0.002), frame))
    parts.append(box("lid_spine", (TW - 0.04, 0.008, 0.008), (0, 0, lz + 0.008), frame))
    # inside: a sand floor, rocks, a basking slate, a log and a water dish
    fz = z0 + 0.13
    parts.append(box("sand", (TW - 0.03, TD - 0.03, 0.11), (0, 0, z0 + 0.075), sand))
    parts.append(ball("mound", 0.14, (-0.18, -0.08, fz - 0.03), sand, scale=(1.6, 1.0, 0.45)))
    for k, (x, y, r) in enumerate(((-0.32, -0.12, 0.07), (-0.24, -0.15, 0.05), (0.12, 0.12, 0.045), (0.06, -0.15, 0.04),
                                   (-0.36, 0.12, 0.035))):
        parts.append(ball(f"rock_{k}", r, (x, y, fz + r * 0.25), rock, scale=(1.2, 1.0, 0.75),
                          rot=(random.random(), random.random(), random.random())))
    parts.append(box("slate", (0.20, 0.14, 0.025), (-0.28, -0.08, fz + 0.075), slate, rot=(0.12, -0.18, 0.35)))
    parts.append(cyl("log", 0.035, 0.38, (0.0, -0.12, fz + 0.03), log, rot=(0, math.pi / 2 - 0.12, 0.30), verts=8))
    ld = Vector((math.cos(0.12) * math.cos(0.30), math.cos(0.12) * math.sin(0.30), math.sin(0.12)))    # the log's axis
    for k, sgn in enumerate((-1, 1)):
        parts.append(cyl(f"log_end_{k}", 0.028, 0.004, Vector((0.0, -0.12, fz + 0.03)) + ld * sgn * 0.191, wood_dark,
                         rot=(0, math.pi / 2 - 0.12, 0.30), verts=8))
    parts.append(cone("dish", 0.08, 0.10, 0.04, (0.31, -0.07, fz + 0.01), dish, verts=12))
    parts.append(cyl("water", 0.085, 0.004, (0.31, -0.07, fz + 0.026), water, verts=12))
    return join(parts, "terrarium")


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


MODELS = {"terrarium": (build, dict(reach=2.6, view=(0.45, 1.0, 1.4)))}
for model, (fn, view) in MODELS.items():
    if ONLY and model != ONLY:
        continue
    o = fn()
    export_glb(o, model)
    export_json(o, model)
    if RENDER:
        render_preview(o, model, **view)
