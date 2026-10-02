"""The hydroponics spoke's Hydro Tray and its holes.

hydro_tray: a long, shallow white channel on a steel stand with five round net-pot holes along it (black grommets), end caps,
a nutrient reservoir tank on the floor underneath, a small pump on the tank lid with a feed hose up to one end of the channel
and a drain pipe back down from the other. The holes are drawn as dark openings: each hole is its own pot object (hydro_hole)
placed into them, so the tray mesh itself shows no plants.
  Footprint: 3 x 1 cells (1.50 x 0.50 m). Origin: bottom centre, on the floor.
  Hole centres (Unity x, y, z, metres from the origin): x = -0.56, -0.28, 0, 0.28, 0.56; y = 0.60 (the channel top); z = 0.

hydro_hole: one net-pot cup, so each hole can be its own pot: a black slotted cup with a flange, filled with clay pebbles.
  Footprint: under one cell (0.15 m across the flange). Origin: the centre of the flange's underside, which rests on the
  channel top, so placing a hole at a hole centre above seats it; the cup hangs 0.08 m below its origin.

Run:  blender -b --python scripts/art/build_hydro.py -- [--out DIR] [--render] [--only hydro_tray]
Writes, per model: <name>.glb, <name>.mesh.json (one part per colour, Unity axes: x right, y up, z towards the front;
left-handed winding) and <name>.png with --render.

Conventions: metres; Blender Z up, +Y is the front. Same pipeline as build_gunrack.py (primitives, join, export); colours are
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
    "pvc": (0.93, 0.93, 0.90),         # the white channel
    "cap": (0.62, 0.64, 0.66),         # end caps
    "steel": (0.72, 0.74, 0.76),       # the stand
    "hole": (0.06, 0.06, 0.07),        # the openings
    "rubber": (0.24, 0.24, 0.26),      # grommets, hoses
    "tank": (0.30, 0.48, 0.72),        # the reservoir
    "lid": (0.36, 0.37, 0.40),
    "pump": (0.88, 0.72, 0.26),        # a yellow pump body
    "netpot": (0.26, 0.26, 0.28),      # the net-pot cup
    "pebble": (0.82, 0.50, 0.34),      # clay pebbles
}
METAL = ("steel",)

L, WID, TOP = 1.45, 0.24, 0.60         # channel length, width, top height
HOLES = [-0.56, -0.28, 0.0, 0.28, 0.56]
HOLE_R = 0.055                          # the opening; the net pot's flange is wider

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



def ball(name, r, loc, m, scale=(1, 1, 1), seg=8, rings=5, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=seg, ring_count=rings, rotation=rot)
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


def build_tray():
    reset()
    pvc, cap, steel, hole, rubber = mat("pvc", 0.4), mat("cap", 0.5), mat("steel", 0.35, 0.8), mat("hole", 0.9), mat("rubber", 0.8)
    tank, lid, pump = mat("tank", 0.5), mat("lid", 0.6), mat("pump", 0.4)
    parts = []
    ch = 0.10                                                   # channel depth
    parts.append(box("channel", (L - 0.04, WID, ch), (0, 0, TOP - ch / 2), pvc, 0.01))
    for sx in (-1, 1):
        parts.append(box(f"endcap_{sx}", (0.03, WID + 0.02, ch + 0.02), (sx * (L / 2 - 0.015), 0, TOP - ch / 2), cap, 0.006))
    for i, x in enumerate(HOLES):
        parts.append(cyl(f"grommet_{i}", HOLE_R + 0.012, 0.006, (x, 0, TOP + 0.003), rubber, verts=14))
        parts.append(cyl(f"hole_{i}", HOLE_R, 0.004, (x, 0, TOP + 0.005), hole, verts=14))
    # the stand: two H frames under the channel ends, a cradle bar under the channel, a long stretcher low down
    lz = TOP - ch
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.12)
        for sy in (-1, 1):
            parts.append(box(f"leg_{sx}_{sy}", (0.03, 0.03, lz), (x, sy * 0.17, lz / 2), steel))
            parts.append(box(f"foot_{sx}_{sy}", (0.06, 0.06, 0.01), (x, sy * 0.17, 0.005), steel))
        parts.append(box(f"cradle_{sx}", (0.04, 0.40, 0.03), (x, 0, lz - 0.015), steel))
        parts.append(box(f"brace_{sx}", (0.025, 0.37, 0.025), (x, 0, 0.12), steel))
    for sy in (-1, 1):
        parts.append(box(f"stretcher_{sy}", (L - 0.24, 0.025, 0.025), (0, sy * 0.17, lz - 0.04), steel))
    # the reservoir tank on the floor, between the frames, its lid and a fill cap
    tw, td, th = 0.80, 0.30, 0.26
    parts.append(box("tank", (tw, td, th), (0, 0, th / 2), tank, 0.015))
    parts.append(box("lid", (tw + 0.02, td + 0.02, 0.025), (0, 0, th + 0.012), lid, 0.006))
    parts.append(cyl("fill_cap", 0.04, 0.03, (0.25, 0.04, th + 0.04), lid, verts=10))
    # the pump on the lid: a yellow body and motor, a feed hose up to the left end of the channel
    px, py = -0.22, 0.07
    parts.append(box("pump_body", (0.12, 0.10, 0.08), (px, py, th + 0.065), pump, 0.01))
    parts.append(cyl("pump_motor", 0.04, 0.08, (px + 0.10, py, th + 0.065), pump, rot=(0, math.pi / 2, 0), verts=10))
    hx, hy = -(L / 2 - 0.06), 0.07
    p0, p1, p2 = (px - 0.06, py, th + 0.07), (hx, hy, th + 0.07), (hx, hy, TOP - ch - 0.005)
    parts.append(rod("hose_a", p0, p1, 0.014, rubber))
    parts.append(rod("hose_b", p1, p2, 0.014, rubber))
    parts.append(ball("hose_bend", 0.014, p1, rubber, seg=6, rings=4))
    # the drain from the right end of the channel back into the tank
    dx = L / 2 - 0.08
    dy = 0.07
    parts.append(rod("drain_a", (dx, dy, TOP - ch - 0.005), (dx, dy, th + 0.10), 0.018, cap))
    parts.append(rod("drain_b", (dx + 0.018, dy, th + 0.10), (tw / 2 - 0.08, dy, th + 0.10), 0.018, cap))
    parts.append(rod("drain_c", (tw / 2 - 0.08, dy, th + 0.118), (tw / 2 - 0.08, dy, th + 0.02), 0.018, cap))
    return join(parts, "hydro_tray")


def build_hole():
    """A net pot: flange (resting on the channel top at z = 0), a tapered cup with slots below it, clay pebbles heaped on top."""
    reset()
    netpot, pebble, hole = mat("netpot", 0.7), mat("pebble", 0.9), mat("hole", 0.9)
    parts = []
    parts.append(cyl("flange", 0.075, 0.008, (0, 0, 0.004), netpot, verts=14))
    parts.append(cone("cup", 0.040, HOLE_R - 0.004, 0.08, (0, 0, -0.04), netpot, verts=14))
    for k in range(6):                                            # dark slots round the cup
        a = k * math.pi / 3
        r = (0.040 + HOLE_R - 0.004) / 2 + 0.001
        parts.append(box(f"slot_{k}", (0.012, 0.004, 0.045), (r * math.cos(a), r * math.sin(a), -0.04), hole,
                         rot=(0, 0, a + math.pi / 2)))
    parts.append(cyl("medium", HOLE_R + 0.004, 0.012, (0, 0, 0.012), pebble, verts=14))
    for k in range(5):
        a = k * 2 * math.pi / 4 + 0.3
        r = 0.0 if k == 0 else 0.032
        parts.append(ball(f"pebble_{k}", 0.014, (r * math.cos(a), r * math.sin(a), 0.020 + (0.006 if k == 0 else 0)), pebble,
                          seg=6, rings=4))
    return join(parts, "hydro_hole")


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


MODELS = {"hydro_tray": (build_tray, dict(reach=1.9)), "hydro_hole": (build_hole, dict(reach=3.0, floor=-0.08))}
for model, (fn, view) in MODELS.items():
    if ONLY and model != ONLY:
        continue
    o = fn()
    export_glb(o, model)
    export_json(o, model)
    if RENDER:
        render_preview(o, model, **view)
