"""The hydroponics spoke's Aeroponic Tower and the water pump for the tap-to-pump hose.

aero_tower: a vertical aeroponic tower about 2 m tall: a white octagonal column in stacked sections on a round reservoir base,
with twelve angled planting ports (four levels of three, each level turned 60 degrees from the one below, so the ports are
staggered round six sides), each port a dark opening for its own pot. A misting pump sits on the reservoir lid (back left) with a
riser pipe up the column to the domed top cap.
  Footprint: 2 x 2 cells (0.80 m across the base). Origin: bottom centre, on the floor.
  Port centres (Unity x, y, z) are printed when the script runs and listed in MODELS.md; each port's opening faces
  outwards and 35 degrees up.

pump: a small electric water pump for the hose from a tap: a yellow motor on a steel base plate, a grey pump housing with a
brass hose spigot facing the front (+Y; the hose connects here) and an outlet pipe rising from the top.
  Footprint: 1 x 1 cell (0.36 x 0.29 m). Origin: bottom centre, on the floor. Hose spigot tip at about (0.10, 0.16, 0.19)
  in Unity axes.

Run:  blender -b --python scripts/art/build_aero.py -- [--out DIR] [--render] [--only pump]
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
    "white": (0.94, 0.94, 0.92),       # the column
    "seam": (0.70, 0.72, 0.74),        # section joints, top cap trim
    "hole": (0.06, 0.06, 0.07),        # port openings
    "base": (0.30, 0.42, 0.52),        # the reservoir
    "lid": (0.52, 0.54, 0.58),
    "pump": (0.88, 0.72, 0.26),        # yellow pump bodies (shared with the hydro tray's pump)
    "housing": (0.58, 0.60, 0.63),
    "steel": (0.74, 0.76, 0.78),
    "brass": (0.86, 0.66, 0.30),
    "rubber": (0.24, 0.24, 0.26),
}
METAL = ("steel", "brass")

COL_R, COL_H = 0.11, 1.62              # column radius (octagon) and height above the base
BASE_R, BASE_H = 0.38, 0.32
LEVELS = [0.70, 1.00, 1.30, 1.60]      # port heights (port mouth centres, roughly)
PORT_TILT = math.radians(35)           # ports point outwards and up

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


def aimed(o, direction):
    """Point an object's local +Z along direction."""
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector(direction).to_track_quat("Z", "Y")
    return o


PORTS = []


def build_tower():
    reset()
    PORTS.clear()
    white, seam, hole, base, lid = mat("white", 0.4), mat("seam", 0.5), mat("hole", 0.9), mat("base", 0.6), mat("lid", 0.6)
    pump, steel, rubber = mat("pump", 0.4), mat("steel", 0.35, 0.8), mat("rubber", 0.8)
    parts = []
    # the reservoir base: a round tub with a lid
    parts.append(cyl("tub", BASE_R, BASE_H, (0, 0, BASE_H / 2), base, verts=16))
    parts.append(cyl("tub_lid", BASE_R + 0.015, 0.04, (0, 0, BASE_H + 0.02), lid, verts=16))
    z0 = BASE_H + 0.04
    # the column in four sections with seams between
    sec = COL_H / 4
    for k in range(4):
        parts.append(cyl(f"section_{k}", COL_R, sec - 0.01, (0, 0, z0 + sec * k + sec / 2), white, verts=8,
                         rot=(0, 0, math.pi / 8)))
        parts.append(cyl(f"seam_{k}", COL_R + 0.008, 0.025, (0, 0, z0 + sec * k + 0.0125), seam, verts=8, rot=(0, 0, math.pi / 8)))
    top = z0 + COL_H
    parts.append(cyl("cap_ring", COL_R + 0.01, 0.03, (0, 0, top + 0.015), seam, verts=8, rot=(0, 0, math.pi / 8)))
    parts.append(cone("cap", COL_R + 0.01, 0.03, 0.10, (0, 0, top + 0.08), white, verts=8, rot=(0, 0, math.pi / 8)))
    # the ports: short angled cups sticking out of the column, open end dark
    for li, z in enumerate(LEVELS):
        for j in range(3):
            a = math.radians(90 + 120 * j + 60 * (li % 2))      # level 0 has a port facing the front (+Y)
            out = Vector((math.cos(a), math.sin(a), 0))
            d = (out * math.cos(PORT_TILT) + Vector((0, 0, math.sin(PORT_TILT)))).normalized()
            root = out * (COL_R - 0.01) + Vector((0, 0, z - 0.05))
            plen = 0.11
            mid = root + d * (plen / 2)
            mouth = root + d * plen
            parts.append(aimed(cone(f"port_{li}_{j}", 0.045, 0.052, plen, mid, white, verts=10), d))
            parts.append(aimed(cyl(f"port_rim_{li}_{j}", 0.056, 0.012, mouth, seam, verts=10), d))
            parts.append(aimed(cyl(f"port_hole_{li}_{j}", 0.045, 0.004, mouth + d * 0.005, hole, verts=10), d))
            PORTS.append(mouth)
    # the misting pump on the lid, back left between ports, and a riser up the column to the cap
    ra = math.radians(240)                                      # a gap between the ports' six directions
    out = Vector((math.cos(ra), math.sin(ra), 0))
    side = Vector((-out.y, out.x, 0))
    pc = out * (COL_R + 0.13)
    parts.append(aimed(box("pump_body", (0.10, 0.14, 0.09), (0, 0, 0), pump, 0.01), (0, 0, 1)))
    parts[-1].rotation_mode = "XYZ"
    parts[-1].rotation_euler = (0, 0, ra)
    parts[-1].location = (pc.x, pc.y, z0 + 0.045)
    mc = pc + side * 0.10
    parts.append(rod("pump_motor", (mc - side * 0.04).to_tuple(), (mc + side * 0.05).to_tuple(), 0.045, pump, verts=10))
    parts[-1].location.z = z0 + 0.05
    rc = out * (COL_R + 0.025)
    parts.append(rod("riser_a", (pc.x - out.x * 0.05, pc.y - out.y * 0.05, z0 + 0.07), (rc.x, rc.y, z0 + 0.07), 0.014, rubber))
    parts.append(rod("riser_b", (rc.x, rc.y, z0 + 0.07), (rc.x, rc.y, top + 0.03), 0.014, rubber))
    parts.append(rod("riser_c", (rc.x, rc.y, top + 0.03), (rc.x * 0.4, rc.y * 0.4, top + 0.07), 0.014, rubber))
    for k in range(3):                                          # clips holding the riser to the column
        c = out * (COL_R + 0.012)
        parts.append(box(f"clip_{k}", (0.03, 0.05, 0.02), (c.x, c.y, z0 + 0.45 + 0.45 * k), seam, rot=(0, 0, ra)))
    obj = join(parts, "aero_tower")
    print("aero_tower ports (Unity x, y, z):", [[round(p.x, 3), round(p.z, 3), round(p.y, 3)] for p in PORTS])
    return obj


def build_pump():
    """A small surface pump: base plate, motor with cooling fins and a fan cowl, a pump housing, a hose spigot in front,
    an outlet pipe on top. The hose spigot faces +Y."""
    reset()
    pump, housing, steel, brass, rubber = mat("pump", 0.4), mat("housing", 0.45, 0.3), mat("steel", 0.35, 0.8), mat("brass", 0.35, 0.6), mat("rubber", 0.8)
    parts = []
    parts.append(box("plate", (0.36, 0.20, 0.02), (0, 0, 0.01), steel, 0.004))
    for sx in (-1, 1):
        parts.append(box(f"foot_{sx}", (0.10, 0.12, 0.04), (sx * 0.07 - 0.03, 0, 0.04), steel))
    mz = 0.13
    parts.append(cyl("motor", 0.07, 0.17, (-0.05, 0, mz), pump, rot=(0, math.pi / 2, 0), verts=12))
    for k in range(4):
        parts.append(cyl(f"fin_{k}", 0.075, 0.008, (-0.11 + 0.035 * k, 0, mz), pump, rot=(0, math.pi / 2, 0), verts=12))
    parts.append(cyl("cowl", 0.068, 0.04, (-0.155, 0, mz), housing, rot=(0, math.pi / 2, 0), verts=12))
    parts.append(box("terminal", (0.06, 0.05, 0.03), (-0.05, 0, mz + 0.08), pump, 0.006))
    # the pump housing (volute) and its spigot, and the outlet
    hx = 0.10
    parts.append(cyl("housing", 0.085, 0.07, (hx, 0, mz), housing, rot=(0, math.pi / 2, 0), verts=12))
    parts.append(cyl("housing_face", 0.06, 0.02, (hx + 0.04, 0, mz), housing, rot=(0, math.pi / 2, 0), verts=12))
    parts.append(cyl("spigot_base", 0.022, 0.04, (hx, 0.09, mz + 0.03), brass, rot=(math.pi / 2, 0, 0), verts=10))
    parts.append(cyl("spigot", 0.013, 0.09, (hx, 0.145, mz + 0.03), brass, rot=(math.pi / 2, 0, 0), verts=8))
    for k in range(3):                                            # barbs on the hose tail
        parts.append(cone(f"barb_{k}", 0.019, 0.013, 0.014, (hx, 0.13 + 0.025 * k, mz + 0.03), brass,
                          rot=(-math.pi / 2, 0, 0), verts=8))
    parts.append(cyl("outlet", 0.02, 0.12, (hx, 0, mz + 0.12), steel, verts=10))
    parts.append(cyl("outlet_nut", 0.028, 0.02, (hx, 0, mz + 0.18), brass, verts=6))
    return join(parts, "pump")


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


MODELS = {"aero_tower": (build_tower, dict(reach=2.2)), "pump": (build_pump, dict(reach=2.6))}
for model, (fn, view) in MODELS.items():
    if ONLY and model != ONLY:
        continue
    o = fn()
    export_glb(o, model)
    export_json(o, model)
    if RENDER:
        render_preview(o, model, **view)
