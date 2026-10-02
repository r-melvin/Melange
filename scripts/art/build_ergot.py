"""The psychedelics spoke's ergot colony and blotter frame.

ergot_colony: the ergot crop for the game's mushroom bed (it replaces the vanilla colony's visuals): a low mound of pale grain
substrate with a stand of pale rye stalks, their seed heads studded with dark purple-black ergot sclerotia (the horn-shaped
spurs, exaggerated in size so they read at game scale), a few leaf blades and a few fallen sclerotia on the grain.
  Footprint: about 0.60 x 0.42 m (sized to sit in a mushroom bed; scale it to the bed's soil area in game). Origin: bottom
  centre, on the bed's soil surface. Height about 0.50 m (awn tips).

blotter_frame: a wooden easel holding a hardboard backing with a blank white paper sheet clipped to it, for spray-painting
blotter designs (the frame carries the game's SpraySurface). The sheet is 3:2 like the SpraySurface's 450 x 300 canvas.
  Footprint: 2 x 1 cells (0.66 x 0.48 m). Origin: bottom centre, on the floor. The sheet faces the front (+Y), leaning back
  8 degrees; its centre, size and normal (Unity axes) are printed when the script runs and listed in MODELS.md.

Run:  blender -b --python scripts/art/build_ergot.py -- [--out DIR] [--render] [--only blotter_frame]
Writes, per model: <name>.glb, <name>.mesh.json (one part per colour, Unity axes: x right, y up, z towards the front;
left-handed winding) and <name>.png with --render.

Conventions: metres; Blender Z up, +Y is the front. Same pipeline as build_gunrack.py (primitives, join, export); colours are
lighter than they look in Blender because in-game lighting is darker.
"""
import bmesh
from mathutils import Matrix, Vector
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
    "grain": (0.90, 0.82, 0.62),       # the grain substrate
    "straw": (0.92, 0.84, 0.56),       # rye stalks
    "head": (0.84, 0.72, 0.42),        # rye seed heads
    "leaf": (0.72, 0.74, 0.46),        # dry green-straw leaves
    "ergot": (0.30, 0.17, 0.32),       # sclerotia: purple-black, lightened
    "pine": (0.80, 0.62, 0.42),        # easel wood
    "board": (0.56, 0.42, 0.30),       # hardboard backing
    "paper": (0.97, 0.97, 0.95),       # the blank sheet
    "steel": (0.70, 0.72, 0.74),       # the clip, the wing nut
}
METAL = ("steel",)

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
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(m)
    return o


def blob(name, p0, p1, r, m, flat=1.0, seg=6, rings=4):
    """An ellipsoid spanning p0 to p1, radius r across."""
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


def spike(name, p0, p1, r, m, verts=5):
    """A cone from a base of radius r at p0 to a point at p1."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    o = cone(name, r, 0.0, d.length, (a + b) / 2, m, verts=verts)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    return o


def leaf(name, base, direction, length, width, m):
    """A flat leaf blade: a thin diamond from base along direction, bent down towards its tip."""
    bm = bmesh.new()
    d = Vector(direction).normalized()
    side = d.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
    b = Vector(base)
    mid = b + d * length * 0.45 + Vector((0, 0, 0.01))
    tip = b + d * length + Vector((0, 0, -length * 0.35))
    pts = (b, mid + side * width, tip, mid - side * width)
    bm.faces.new([bm.verts.new(v) for v in pts])
    bm.faces.new([bm.verts.new(v) for v in reversed(pts)])      # two-sided: a back face on its own vertices
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
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


def build_colony():
    reset()
    random.seed(11)
    grain, straw, head, leaf_m, ergot = mat("grain", 0.95), mat("straw", 0.8), mat("head", 0.8), mat("leaf", 0.8), mat("ergot", 0.5)
    parts = []
    CW, CD = 0.60, 0.42
    parts.append(ball("mound", 1.0, (0, 0, 0.0), grain, scale=(CW / 2, CD / 2, 0.06), seg=12, rings=6))
    # flatten the mound's underside onto the origin plane
    for v in parts[0].data.vertices:
        v.co.z = max(v.co.z, 0.0)
    # a few loose grains on the mound
    for k in range(10):
        a, r = random.uniform(0, 2 * math.pi), random.uniform(0.05, 0.8)
        x, y = math.cos(a) * r * CW / 2 * 0.85, math.sin(a) * r * CD / 2 * 0.85
        z = 0.06 * math.sqrt(max(0.0, 1 - (x / (CW / 2)) ** 2 - (y / (CD / 2)) ** 2))
        parts.append(ball(f"grain_{k}", 0.008, (x, y, z), head, scale=(1.6, 1.0, 0.8), seg=5, rings=3,
                          rot=(0, 0, random.uniform(0, math.pi))))
    # the rye: stalks in loose rows, each with a seed head and two or three sclerotia
    n = 0
    for row in range(4):
        for col in range(5):
            x = -CW / 2 + 0.08 + (CW - 0.16) * col / 4 + random.uniform(-0.03, 0.03)
            y = -CD / 2 + 0.07 + (CD - 0.14) * row / 3 + random.uniform(-0.025, 0.025) + (0.02 if col % 2 else -0.02)
            if (x / (CW / 2)) ** 2 + (y / (CD / 2)) ** 2 > 0.8:
                continue
            z0 = 0.06 * math.sqrt(max(0.0, 1 - (x / (CW / 2)) ** 2 - (y / (CD / 2)) ** 2)) - 0.01
            h = random.uniform(0.24, 0.32)
            lean = Vector((random.uniform(-0.06, 0.06) + x * 0.15, random.uniform(-0.06, 0.06) + y * 0.15, 1)).normalized()
            base = Vector((x, y, z0))
            top = base + lean * h
            parts.append(rod(f"stalk_{n}", base, top, 0.0045, straw, verts=5))
            hd = (lean + Vector((random.uniform(-0.15, 0.15), random.uniform(-0.15, 0.15), 0))).normalized()
            htop = top + hd * 0.095
            parts.append(blob(f"head_{n}", top - hd * 0.005, htop, 0.0095, head))
            side0 = hd.cross(Vector((0, 0, 1)))
            side0 = side0.normalized() if side0.length > 1e-4 else Vector((1, 0, 0))
            for w in (-1, 0, 1):                                    # long awns fanning up from the head
                ad = (hd + side0 * 0.25 * w).normalized()
                parts.append(spike(f"awn_{n}_{w}", htop - hd * 0.03, htop + ad * 0.06, 0.003, head, verts=3))
            # sclerotia: dark horns poking out of the head, angled up and out
            for s in range(random.choice((2, 3, 3))):
                ang = random.uniform(0, 2 * math.pi)
                side = Matrix.Rotation(ang, 3, hd) @ side0
                at = top + hd * random.uniform(0.02, 0.075)
                out = (side * 0.75 + hd * 0.65).normalized()
                length = random.uniform(0.028, 0.042)
                parts.append(spike(f"ergot_{n}_{s}", at - out * 0.004, at + out * length, 0.0065, ergot, verts=5))
            if n % 3 == 0:
                d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), 0.6))
                parts.append(leaf(f"leaf_{n}", base + lean * h * 0.35, d, 0.13, 0.012, leaf_m))
            n += 1
    # a few fallen sclerotia lying on the grain
    for k, (x, y, a) in enumerate(((0.14, 0.13, 0.4), (-0.18, 0.12, 2.0), (0.05, -0.15, 1.1), (-0.06, 0.16, 2.7))):
        z = 0.06 * math.sqrt(max(0.0, 1 - (x / (CW / 2)) ** 2 - (y / (CD / 2)) ** 2)) + 0.004
        d = Vector((math.cos(a), math.sin(a), 0.1))
        parts.append(spike(f"fallen_{k}", Vector((x, y, z)) - d * 0.015, Vector((x, y, z)) + d * 0.02, 0.006, ergot))
    print(f"ergot_colony: {n} stalks")
    return join(parts, "ergot_colony")


def build_frame():
    """A studio easel: two front legs and a back leg hinged at the top, a ledge with a lip across the front legs, a top
    clamp bar sliding on a centre mast, a hardboard backing and the paper sheet clipped to it."""
    reset()
    pine, board, paper, steel = mat("pine", 0.7), mat("board", 0.8), mat("paper", 0.9), mat("steel", 0.35, 0.8)
    parts = []
    lean = math.radians(8)
    up = Vector((0, -math.sin(lean), math.cos(lean)))          # up along the easel's face, leaning back
    fwd = Vector((0, math.cos(lean), math.sin(lean)))          # the face's normal
    H = 1.55
    foot_y = 0.20
    # front legs splay out at the bottom and meet the mast near the top
    for sx in (-1, 1):
        b = Vector((sx * 0.30, foot_y, 0.0))
        t = Vector((sx * 0.07, foot_y, 0.0)) + up * (H - 0.05)
        parts.append(rod(f"leg_{sx}", b + Vector((0, 0, 0.01)), t, 0.018, pine, verts=6))
        parts.append(box(f"foot_{sx}", (0.05, 0.05, 0.02), (b.x, b.y, 0.01), pine))
    mast_b = Vector((0, foot_y, 0.0)) + up * 0.55
    mast_t = Vector((0, foot_y, 0.0)) + up * H
    parts.append(rod("mast", mast_b, mast_t, 0.016, pine, verts=6))
    # the back leg from the top hinge down to the floor behind
    hinge = Vector((0, foot_y, 0.0)) + up * (H - 0.08) - fwd * 0.03
    parts.append(rod("back_leg", hinge, (0, -0.24, 0.012), 0.016, pine, verts=6))
    parts.append(box("back_foot", (0.05, 0.05, 0.02), (0, -0.24, 0.01), pine))
    parts.append(cyl("hinge", 0.022, 0.06, hinge, steel, rot=(0, math.pi / 2, 0), verts=8))
    # a cross brace between the front legs low down, and a stay from it to the back leg
    brace_z = 0.32
    bp = Vector((0, foot_y, 0.0)) + up * brace_z
    parts.append(rod("brace", bp + Vector((-0.25, 0, 0)), bp + Vector((0.25, 0, 0)), 0.012, pine, verts=6))
    # the ledge (tray) across the front legs, with a lip
    ledge_s = 0.72
    lc = Vector((0, foot_y, 0.0)) + up * ledge_s + fwd * 0.03
    rot = (lean, 0, 0)                                          # leans back with the legs
    parts.append(box("ledge", (0.62, 0.07, 0.02), lc, pine, 0.003, rot=rot))
    parts.append(box("ledge_lip", (0.62, 0.012, 0.03), lc + fwd * 0.0 + Vector((0, 0.035 * math.cos(lean), 0.025)), pine, rot=rot))
    # the backing board standing on the ledge, the sheet on it, the clamp bar on top
    BW, BH = 0.66, 0.46
    SW, SH = 0.60, 0.40                                         # 3:2
    bc = lc + up * (0.01 + BH / 2) + fwd * 0.0
    parts.append(box("backing", (BW, 0.012, BH), bc, board, 0.002, rot=rot))
    sc = bc + fwd * 0.0075
    parts.append(box("sheet", (SW, 0.002, SH), sc, paper, rot=rot))
    clamp = bc + up * (BH / 2 + 0.012) + fwd * 0.012
    parts.append(box("clamp", (0.30, 0.035, 0.03), clamp, pine, 0.004, rot=rot))
    parts.append(box("clip", (0.06, 0.004, 0.05), clamp + fwd * 0.012 - up * 0.03, steel, rot=rot))
    parts.append(cyl("wing_nut", 0.012, 0.02, clamp + fwd * 0.026, steel, rot=(lean - math.pi / 2, 0, 0), verts=6))
    obj = join(parts, "blotter_frame")
    print("blotter_frame sheet (Unity): centre", [round(sc.x, 3), round(sc.z, 3), round(sc.y + 0.0015, 3)],
          "size", [SW, SH], "normal", [round(fwd.x, 3), round(fwd.z, 3), round(fwd.y, 3)])
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


MODELS = {"ergot_colony": (build_colony, dict(reach=1.9, view=(0.55, 1.0, 0.9))),
          "blotter_frame": (build_frame, dict(reach=2.0))}
for model, (fn, view) in MODELS.items():
    if ONLY and model != ONLY:
        continue
    o = fn()
    export_glb(o, model)
    export_json(o, model)
    if RENDER:
        render_preview(o, model, **view)
