"""The mixer spoke's three multi-ingredient mixing stations: mixer2, mixer3 and mixer4, one family of industrial mixer benches
(the vanilla Mixing Station Mk2 is a worktop mixer). Each is a steel bench with a lower shelf, a painted machine body along the
back carrying a row of numbered ingredient hoppers (2, 3 or 4, numbered from the player's left), a product input chute on
the player's left, a mixing drum in the middle, an output tray on the player's right and a small control panel. Each tier is wider, taller and has its own body colour.

Footprints (0.5 m grid cells, width x depth):
  mixer2   2 x 2 cells (0.95 x 0.85 m), about the Mk2's
  mixer3   3 x 2 cells (1.40 x 0.90 m)
  mixer4   4 x 2 cells (1.90 x 0.95 m)

Run:  blender -b --python scripts/art/build_mixers.py -- [--out DIR] [--render] [--only mixer3]
Writes, per model:
  <name>.glb          for previews and other tools
  <name>.mesh.json    what the mod embeds: one part per colour, Unity axes (x right, y up, z towards the front), left-handed winding
  <name>.png          a preview render (with --render)

Conventions: metres; Blender Z up, +Y is the front (the side the player works from). The origin is the bottom centre of the
footprint, on the floor. Same pipeline as build_gunrack.py (primitives, join, export); colours are lighter than they look in
Blender because in-game lighting is darker.
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
    "steel": (0.78, 0.80, 0.82),       # stainless worktop, hoppers, drum
    "frame": (0.30, 0.31, 0.33),       # bench legs and shelf
    "body2": (0.36, 0.66, 0.72),       # each tier's machine body colour
    "body3": (0.42, 0.52, 0.82),
    "body4": (0.82, 0.44, 0.40),
    "plate": (0.95, 0.95, 0.92),       # number plates
    "ink": (0.08, 0.08, 0.09),         # numbers, hopper mouths, tray well
    "input": (0.40, 0.74, 0.42),       # the product input chute
    "output": (0.95, 0.66, 0.25),      # the output tray
    "panel": (0.20, 0.21, 0.23),
    "button_g": (0.30, 0.90, 0.35),
    "button_r": (0.95, 0.25, 0.22),
}
METAL = ("steel",)

# per tier: footprint width, depth, worktop height, body height, hopper top radius, drum radius
TIERS = {
    "mixer2": dict(n=2, W=0.95, D=0.85, top=0.90, body_h=0.34, hop_r=0.11, drum_r=0.15, body="body2"),
    "mixer3": dict(n=3, W=1.40, D=0.90, top=0.90, body_h=0.40, hop_r=0.12, drum_r=0.18, body="body3"),
    "mixer4": dict(n=4, W=1.90, D=0.95, top=0.90, body_h=0.46, hop_r=0.13, drum_r=0.21, body="body4"),
}


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


def build(name):
    t = TIERS[name]
    n, W, D, top, body_h, hop_r, drum_r = t["n"], t["W"], t["D"], t["top"], t["body_h"], t["hop_r"], t["drum_r"]
    reset()
    steel, frame, body, plate, ink = mat("steel", 0.35, 0.8), mat("frame", 0.5), mat(t["body"], 0.5), mat("plate", 0.6), mat("ink", 0.8)
    inp, outp, panel, bg, br = mat("input", 0.5), mat("output", 0.5), mat("panel", 0.6), mat("button_g", 0.4), mat("button_r", 0.4)
    parts = []
    # the bench: four legs, a lower shelf, a worktop with a slight lip
    lw, tt = 0.05, 0.05
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(box(f"leg_{sx}_{sy}", (lw, lw, top - tt), (sx * (W / 2 - 0.05), sy * (D / 2 - 0.05), (top - tt) / 2), frame))
    parts.append(box("shelf", (W - 0.08, D - 0.08, 0.03), (0, 0, 0.18), frame))
    parts.append(box("worktop", (W, D, tt), (0, 0, top - tt / 2), steel, 0.008))
    # the machine body along the back
    bd = 0.30
    by = -D / 2 + bd / 2 + 0.02
    parts.append(box("body", (W - 0.04, bd, body_h), (0, by, top + body_h / 2), body, 0.015))
    parts.append(box("body_cap", (W - 0.02, bd + 0.02, 0.03), (0, by, top + body_h + 0.015), steel, 0.005))
    # the hoppers, numbered, in a row on top of the body
    span = W - 0.30
    hop_h = 0.26 + 0.02 * n
    xs = [(span / 2 - span * i / (n - 1)) if n > 1 else 0.0 for i in range(n)]   # 1 on the player's left (+X)
    body_top = top + body_h + 0.03
    for i, x in enumerate(xs):
        parts.append(cyl(f"neck_{i}", 0.045, 0.06, (x, by, body_top + 0.03), steel))
        parts.append(cone(f"hopper_{i}", 0.05, hop_r, hop_h, (x, by, body_top + 0.06 + hop_h / 2), steel))
        parts.append(cyl(f"rim_{i}", hop_r + 0.008, 0.025, (x, by, body_top + 0.06 + hop_h), body))
        parts.append(cyl(f"mouth_{i}", hop_r - 0.012, 0.004, (x, by, body_top + 0.06 + hop_h + 0.0145), ink))
        # a number plate hung on the front of each hopper, tilted to the cone's slope
        slope = math.atan((hop_r - 0.05) / hop_h)
        pz = body_top + 0.06 + hop_h * 0.55
        pr = 0.05 + (hop_r - 0.05) * 0.55 + 0.006
        parts.append(box(f"plate_{i}", (0.09, 0.008, 0.09), (x, by + pr, pz), plate, 0.004, rot=(-slope, 0, 0)))
        parts.append(label(f"num_{i}", str(i + 1), 0.075, (x, by + pr + 0.006 * math.cos(slope), pz - 0.006 * math.sin(slope)), ink,
                           rot=(math.pi / 2 + slope, 0, math.pi)))
    # a feed manifold from the body down into the drum
    work_y = by + bd / 2 + (D / 2 - (by + bd / 2)) * 0.45      # middle of the free worktop, front to back
    drum_h = 0.22 + 0.04 * n
    drum_y = by + bd / 2 + drum_r + 0.04
    parts.append(cyl("drum", drum_r, drum_h, (0, drum_y, top + drum_h / 2), steel, verts=14))
    parts.append(cyl("drum_band", drum_r + 0.01, 0.03, (0, drum_y, top + drum_h * 0.35), body, verts=14))
    parts.append(cyl("drum_lid", drum_r + 0.012, 0.025, (0, drum_y, top + drum_h + 0.012), steel, verts=14))
    parts.append(cyl("motor", drum_r * 0.45, 0.10, (0, drum_y, top + drum_h + 0.075), panel, verts=10))
    feed_z = top + body_h * 0.25
    parts.append(rod("feed", (0, by + bd / 2 - 0.01, feed_z), (0, drum_y - drum_r + 0.02, feed_z), 0.035, steel))
    # the product input chute on the player's left (+X): a square funnel on a short stand, piped into the body
    cx = W / 2 - 0.17
    cy = work_y - 0.04
    parts.append(box("chute_stand", (0.10, 0.10, 0.10), (cx, cy, top + 0.05), inp))
    parts.append(cone("chute", 0.07, 0.15, 0.16, (cx, cy, top + 0.18), inp, rot=(0, 0, math.pi / 4), verts=4))
    parts.append(box("chute_mouth", (0.15, 0.15, 0.004), (cx, cy, top + 0.262), ink))
    parts.append(rod("chute_pipe", (cx, cy, top + 0.06), (cx, by + bd / 2 - 0.01, top + 0.06), 0.03, inp))
    # the output tray on the player's right (-X), fed by a short spout sloping down from the drum
    ox = max(-(drum_r + 0.24), -W / 2 + 0.16)
    oy = drum_y + 0.04
    parts.append(box("tray", (0.28, 0.24, 0.05), (ox, oy, top + 0.025), outp, 0.006))
    parts.append(box("tray_well", (0.24, 0.20, 0.004), (ox, oy, top + 0.051), ink))
    parts.append(rod("spout", (-drum_r + 0.02, drum_y, top + 0.16), (ox + 0.06, oy, top + 0.10), 0.025, steel))
    # the control panel: a sloped box on the body's front, right of the plates, two buttons
    parts.append(box("panel", (0.14, 0.05, 0.12), (-W / 2 + 0.12, by + bd / 2 + 0.02, top + body_h * 0.6), panel, 0.006,
                     rot=(math.radians(-20), 0, 0)))
    parts.append(cyl("btn_g", 0.015, 0.02, (-W / 2 + 0.09, by + bd / 2 + 0.05, top + body_h * 0.6 + 0.02), bg,
                     rot=(math.radians(70), 0, 0), verts=8))
    parts.append(cyl("btn_r", 0.015, 0.02, (-W / 2 + 0.15, by + bd / 2 + 0.05, top + body_h * 0.6 + 0.02), br,
                     rot=(math.radians(70), 0, 0), verts=8))
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)     # mesh coordinates = model space
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


def render_preview(obj, name):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "CYCLES"
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.55, 0.58, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    scene.world = world
    floor = bpy.data.materials.new("floor")
    floor.use_nodes = True
    floor.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.45, 0.44, 0.42, 1)
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(floor)
    bpy.ops.object.light_add(type="SUN", location=(3, 4, 6))
    sun = bpy.context.active_object
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(150))
    dims = obj.dimensions
    target = Vector((0, 0, dims.z * 0.5))
    reach = max(dims.x, dims.y, dims.z) * 1.9 + 0.5
    cam_loc = target + Vector((0.55, 1.0, 0.6)).normalized() * reach
    bpy.ops.object.camera_add(location=cam_loc)
    cam = bpy.context.active_object
    cam.rotation_euler = (target - cam_loc).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = os.path.join(OUT, f"{name}.png")
    bpy.ops.render.render(write_still=True)


for model in TIERS:
    if ONLY and model != ONLY:
        continue
    o = build(model)
    export_glb(o, model)
    export_json(o, model)
    if RENDER:
        render_preview(o, model)
