"""Dafydd "Turnip Night" Seabiscuit's speedboat for the smuggling spoke: a low-poly go-fast boat (about 7 m), a V hull with a
raised bow, a deck, a centre console with a windscreen, two seats, twin outboards, a bow rail and a black flag with crossed
bones on a stern pole. Low poly (a few thousand triangles at most).

Run:  blender -b --python scripts/art/build_speedboat.py -- [--out DIR] [--render]
Writes:
  speedboat.glb          for previews and other tools
  speedboat.mesh.json    what the mod embeds: one part per colour, Unity axes (x right, y up, z towards the bow), left-handed winding
  speedboat.png          a preview render (with --render)

Conventions: metres; Blender Z up, +Y is the bow. The origin is on the waterline, midships, on the centre line, so the boat
floats at its origin. Same pipeline as build_gunrack.py (primitives, join, export); colours are lighter than they look in
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
os.makedirs(OUT, exist_ok=True)

L, B = 7.0, 2.3                     # length overall, beam
COLORS = {
    "hull": (0.92, 0.92, 0.90),      # white topsides
    "bottom": (0.62, 0.12, 0.12),    # red antifouling below the chine
    "stripe": (0.10, 0.10, 0.12),    # dark boot stripe at the chine
    "deck": (0.72, 0.62, 0.46),      # light teak
    "console": (0.85, 0.85, 0.83),
    "glass": (0.45, 0.62, 0.70),     # tinted screen, opaque
    "seat": (0.20, 0.20, 0.22),
    "engine": (0.14, 0.14, 0.16),
    "steel": (0.70, 0.72, 0.74),
    "flag": (0.06, 0.06, 0.06),
    "bones": (0.95, 0.95, 0.92),
}
METAL = ("steel",)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


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
        mod.segments = 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    o.data.materials.append(m)
    return o


def cyl(name, r, h, loc, m, rot=(0, 0, 0), verts=10):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=h, location=loc, rotation=rot, vertices=verts)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(m)
    return o


def station(t):
    """The hull's cross-section at t (0 = transom, 1 = bow): half-beam, keel depth below the waterline, sheer height above it."""
    half = B / 2 * (1.0 if t < 0.35 else max(0.02, 1.0 - ((t - 0.35) / 0.65) ** 1.8))   # a long, sharp bow
    keel = 0.50 * (1.0 if t < 0.55 else max(0.04, 1.0 - (t - 0.55) / 0.45 * 0.96))
    sheer = 0.38 + 0.30 * max(0.0, (t - 0.45) / 0.55) ** 2      # low freeboard; the bow rises
    return half, keel, sheer


def hull():
    """A V hull lofted through cross-sections: sheer, chine (split by a boot stripe), keel. The deck closes the top."""
    hull_m, bottom_m, stripe_m, deck_m = mat("hull", 0.35), mat("bottom", 0.7), mat("stripe", 0.5), mat("deck", 0.8)
    mesh = bpy.data.meshes.new("hull")
    obj = bpy.data.objects.new("hull", mesh)
    bpy.context.collection.objects.link(obj)
    for m in (hull_m, bottom_m, stripe_m, deck_m):
        obj.data.materials.append(m)
    HULL, BOTTOM, STRIPE, DECK = 0, 1, 2, 3
    bm = bmesh.new()
    n = 14
    rings = []
    for i in range(n + 1):
        t = i / n
        y = -L / 2 + t * L
        half, keel, sheer = station(t)
        chine_z = -keel * 0.20                                  # the chine sits just under the waterline
        ring = [(-half, y, sheer), (-half * 0.84, y, chine_z + 0.07), (-half * 0.82, y, chine_z),
                (0.0, y, -keel),
                (half * 0.82, y, chine_z), (half * 0.84, y, chine_z + 0.07), (half, y, sheer)]   # topsides flare out
        rings.append([bm.verts.new(v) for v in ring])
    bm.verts.ensure_lookup_table()
    seg_mat = [HULL, STRIPE, BOTTOM, BOTTOM, STRIPE, HULL]
    for i in range(n):
        a, b = rings[i], rings[i + 1]
        for k in range(6):
            f = bm.faces.new((a[k], a[k + 1], b[k + 1], b[k]))
            f.material_index = seg_mat[k]
        deck = bm.faces.new((a[6], a[0], b[0], b[6]))            # the deck, flush with the sheer
        deck.material_index = DECK
    transom = bm.faces.new(list(reversed(rings[0])))            # the flat stern
    transom.material_index = HULL
    bow = bm.faces.new(rings[-1])
    bow.material_index = HULL
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    return obj


def rod(name, p0, p1, r, m, verts=6):
    """A cylinder from p0 to p1."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    o = cyl(name, r, d.length, (a + b) / 2, m, verts=verts)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    return o


def build():
    reset()
    parts = [hull()]
    console, glass, seat, engine, steel, flag, bones = (mat("console", 0.4), mat("glass", 0.1), mat("seat", 0.9), mat("engine", 0.5),
                                                        mat("steel", 0.3, 0.9), mat("flag", 0.95), mat("bones", 0.9))
    deck_z = 0.38
    # the centre console and its raked windscreen
    parts.append(box("console", (0.9, 0.7, 0.85), (0, 0.4, deck_z + 0.42), console, 0.03))
    parts.append(box("screen", (1.0, 0.04, 0.42), (0, 0.78, deck_z + 1.02), glass, rot=(math.radians(-25), 0, 0)))
    parts.append(cyl("wheel", 0.16, 0.04, (0, 0.02, deck_z + 0.95), steel, rot=(math.radians(65), 0, 0), verts=12))
    # two seats behind the console
    for x in (-0.45, 0.45):
        parts.append(box(f"seat_{x > 0}", (0.6, 0.55, 0.35), (x, -0.55, deck_z + 0.18), seat, 0.04))
        parts.append(box(f"back_{x > 0}", (0.6, 0.12, 0.5), (x, -0.85, deck_z + 0.5), seat, 0.04))
    # twin outboards on the transom
    for x in (-0.45, 0.45):
        parts.append(box(f"cowl_{x > 0}", (0.42, 0.5, 0.6), (x, -L / 2 - 0.3, deck_z + 0.15), engine, 0.05))
        parts.append(box(f"leg_{x > 0}", (0.14, 0.2, 0.8), (x, -L / 2 - 0.32, -0.3), engine))
        parts.append(cyl(f"prop_{x > 0}", 0.16, 0.05, (x, -L / 2 - 0.32, -0.72), steel, rot=(math.radians(90), 0, 0), verts=8))
    # a bow rail: stanchions either side of the foredeck, joined by rails, meeting at the bow
    for side in (-1, 1):
        tops = []
        for t in (0.6, 0.7, 0.8, 0.88):
            half, _, sheer = station(t)
            y = -L / 2 + t * L
            x = side * max(0.06, half - 0.1)
            parts.append(cyl(f"post_{side}_{t}", 0.018, 0.32, (x, y, sheer + 0.16), steel, verts=6))
            tops.append((x, y, sheer + 0.32))
        for i in range(len(tops) - 1):
            parts.append(rod(f"rail_{side}_{i}", tops[i], tops[i + 1], 0.016, steel))
        tip = station(0.95)
        parts.append(rod(f"railtip_{side}", tops[-1], (0, -L / 2 + 0.95 * L, tip[2] + 0.3), 0.016, steel))
    # Turnip Night's flag: a black flag with crossed bones on a stern pole
    pole_x, pole_y = 0.85, -L / 2 + 0.25
    parts.append(cyl("pole", 0.025, 1.8, (pole_x, pole_y, deck_z + 0.9), steel, verts=6))
    parts.append(box("flag", (0.02, 0.6, 0.4), (pole_x, pole_y - 0.32, deck_z + 1.55), flag))
    for a in (35, -35):
        parts.append(box(f"bone_{a}", (0.03, 0.42, 0.05), (pole_x + 0.015, pole_y - 0.32, deck_z + 1.55), bones, rot=(math.radians(a), 0, 0)))
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    boat = bpy.context.active_object
    boat.name = "Speedboat"
    return boat


def export_glb(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "speedboat.glb"), use_selection=True, export_format="GLB")


def export_json(obj):
    """One part per material colour, triangulated; Blender (x, y, z) -> Unity (x, z, y). That axis swap mirrors, so each triangle's
    winding is reversed to keep the faces pointing outwards in Unity's left-handed space."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    parts = {}
    for face in bm.faces:
        key = obj.data.materials[face.material_index].name
        part = parts.setdefault(key, {"color": list(COLORS[key]), "metal": key in METAL, "v": [], "n": [], "t": []})
        base = len(part["v"]) // 3
        for loop in face.loops:
            co = loop.vert.co
            part["v"] += [round(co.x, 5), round(co.z, 5), round(co.y, 5)]
            nn = face.normal if not face.smooth else loop.vert.normal
            part["n"] += [round(nn.x, 4), round(nn.z, 4), round(nn.y, 4)]
        part["t"] += [base, base + 2, base + 1]
    bm.free()
    data = {"name": "speedboat", "size": [B, 2.0, L], "parts": list(parts.values())}
    with open(os.path.join(OUT, "speedboat.mesh.json"), "w") as f:
        json.dump(data, f, separators=(",", ":"))
    tris = sum(len(p["t"]) // 3 for p in parts.values())
    print(f"speedboat: {len(parts)} parts, {tris} triangles")


def render_preview(obj):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "CYCLES"
    scene.render.resolution_x, scene.render.resolution_y = 1400, 900
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.62, 0.70, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    scene.world = world
    water = bpy.data.materials.new("water")
    water.use_nodes = True
    water.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.12, 0.25, 0.32, 1)
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(water)
    bpy.ops.object.light_add(type="SUN", location=(4, -4, 8))
    sun = bpy.context.active_object
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(35))
    bpy.ops.object.camera_add(location=(7.0, 6.0, 5.5))
    cam = bpy.context.active_object
    direction = (-cam.location.x, -cam.location.y, 0.4 - cam.location.z)
    cam.rotation_euler = (math.atan2(math.hypot(direction[0], direction[1]), -direction[2]), 0,
                          math.atan2(direction[1], direction[0]) - math.pi / 2)
    scene.camera = cam
    scene.render.filepath = os.path.join(OUT, "speedboat.png")
    bpy.ops.render.render(write_still=True)


boat = build()
export_glb(boat)
export_json(boat)
if RENDER:
    render_preview(boat)
