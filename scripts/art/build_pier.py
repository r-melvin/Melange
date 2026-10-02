"""Turnip Night's pier for the smuggling spoke: a weathered timber floating pontoon (2.5 x 9 m) on the water line along the
Docks quay wall, a timber staircase with handrails down the wall face from a small landing on the quay top, cleats and two
timber bollards where the speedboat ties up, fenders on the boat side and two guide piles in hoops at the pontoon's ends.

Run:  blender -b --python scripts/art/build_pier.py -- [--out DIR] [--render]
Writes:
  pier.glb          for previews and other tools
  pier.mesh.json    what the mod embeds: one part per colour, Unity axes (x out into the basin, y up, z along the quay),
                    left-handed winding
  pier.png          a preview from the basin, and pier_stair.png from the quay top (with --render; the quay, the water and
                    the speedboat (out/speedboat.glb, if built) are render-only and not exported)

Conventions: metres; Blender Z up. The origin is on the quay edge (the wall face) at quay-top height, abreast the pontoon's
middle; +X is out into the basin, +Y along the quay; the stair's head is at -Y. The numbers are MelangeSmuggling/Logic/
PierLayout.cs's (the game's colliders use them): change both together. Same pipeline as build_gunrack.py; colours are lighter
than they look in Blender because in-game lighting is darker.
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

# ---- PierLayout.cs ----
WALL_GAP, WIDTH, LENGTH = 0.1, 2.5, 9.0
DROP, FREEBOARD = 3.65, 0.35
STAIR_W, RISERS, TREAD, HEAD = 1.0, 20, 0.28, -4.2
LANDING_IN, LANDING_D, LANDING_TOP = -0.6, 1.0, 0.03
RAIL_H = 1.0
RISER = DROP / RISERS
DECK = -DROP
WATER = DECK - FREEBOARD
OUTER = WALL_GAP + WIDTH
X0, X1 = WALL_GAP, WALL_GAP + STAIR_W
FOOT = HEAD + RISERS * TREAD

COLORS = {
    "plank": (0.70, 0.64, 0.54),     # sun-bleached deck boards
    "plank2": (0.60, 0.58, 0.54),    # greyer, older boards
    "timber": (0.58, 0.49, 0.39),    # frames, stringers, rails
    "pile": (0.40, 0.34, 0.28),      # tarred piles and float frames
    "float": (0.26, 0.28, 0.30),     # dark float drums
    "steel": (0.70, 0.72, 0.74),     # galvanised cleats and hoops
    "fender": (0.20, 0.24, 0.34),    # navy fenders
    "rope": (0.82, 0.74, 0.56),
}
METAL = ("steel",)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


_mats = {}


def mat(key, rough=0.8, metal=0.0):
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


def box(name, size, loc, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(m)
    return o


def span(name, xa, xb, ya, yb, za, zb, m):
    """An axis-aligned box from its min and max corners."""
    return box(name, (xb - xa, yb - ya, zb - za), ((xa + xb) / 2, (ya + yb) / 2, (za + zb) / 2), m)


def cyl(name, r, h, loc, m, rot=(0, 0, 0), verts=10):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=h, location=loc, rotation=rot, vertices=verts)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(m)
    return o


def beam(name, p0, p1, w, h, m):
    """A rectangular timber from p0 to p1, w across (horizontal) and h deep."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    o = box(name, (w, d.length, h), (0, 0, 0), m)          # box() applies its transform, so place it after turning it
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0.0, 1.0, 0.0)).rotation_difference(d)
    o.location = (a + b) / 2
    return o


def nosing(y):
    """Height of the stair's nosing line at y (along the quay)."""
    return -(y - HEAD) * RISER / TREAD


def pontoon(parts):
    plank, plank2, timber, pile, flt, steel, fender = (mat("plank"), mat("plank2"), mat("timber"), mat("pile"), mat("float", 0.6),
                                                       mat("steel", 0.35, 0.9), mat("fender", 0.5))
    # deck boards across the pontoon, small gaps, a few boards greyer (weathered)
    n = int(LENGTH / 0.2)
    pw = LENGTH / n
    for i in range(n):
        y0 = -LENGTH / 2 + i * pw
        m = plank2 if (i * 7) % 5 in (1, 3) else plank
        parts.append(span(f"board_{i}", X0, OUTER, y0 + 0.012, y0 + pw - 0.012, DECK - 0.05, DECK, m))
    # bearers under the boards and a fascia round the edge
    for x in (X0 + 0.1, (X0 + OUTER) / 2, OUTER - 0.1):
        parts.append(span(f"bearer_{x:.2f}", x - 0.08, x + 0.08, -LENGTH / 2, LENGTH / 2, DECK - 0.27, DECK - 0.05, timber))
    parts.append(span("fascia_in", X0 - 0.05, X0, -LENGTH / 2, LENGTH / 2, DECK - 0.3, DECK, timber))
    parts.append(span("fascia_out", OUTER, OUTER + 0.06, -LENGTH / 2, LENGTH / 2, DECK - 0.32, DECK + 0.02, timber))
    for s in (-1, 1):
        parts.append(span(f"fascia_end_{s}", X0 - 0.05, OUTER + 0.06, s * LENGTH / 2 - 0.05, s * LENGTH / 2 + 0.05, DECK - 0.3, DECK, timber))
    # a low rubbing kerb on the boat side
    parts.append(span("kerb", OUTER - 0.1, OUTER + 0.06, -LENGTH / 2, LENGTH / 2, DECK, DECK + 0.1, pile))
    # floats: two rows of dark drums under the bearers, nearly all under water
    for x in (X0 + 0.55, OUTER - 0.55):
        for j in range(4):
            y = -LENGTH / 2 + 1.2 + j * 2.2
            parts.append(cyl(f"float_{x:.1f}_{j}", 0.32, 1.8, (x, y, DECK - 0.28 - 0.32), flt, rot=(math.radians(90), 0, 0), verts=12))
    # rails across both ends (posts and two rails), so nobody steps off the end
    for s in (-1, 1):
        y = s * (LENGTH / 2 - 0.05)
        for x in (X0 + 0.05, (X0 + OUTER) / 2, OUTER - 0.05):
            parts.append(span(f"endpost_{s}_{x:.2f}", x - 0.05, x + 0.05, y - 0.05, y + 0.05, DECK, DECK + RAIL_H, timber))
        parts.append(span(f"endrail_{s}", X0, OUTER, y - 0.05, y + 0.05, DECK + RAIL_H - 0.08, DECK + RAIL_H, timber))
        parts.append(span(f"endmid_{s}", X0, OUTER, y - 0.03, y + 0.03, DECK + 0.48, DECK + 0.56, timber))
    # cleats where the boat's bow and stern lines go, with a coil of rope on one
    for y in (-2.8, 2.8):
        parts.append(span(f"cleat_base_{y}", OUTER - 0.32, OUTER - 0.2, y - 0.08, y + 0.08, DECK, DECK + 0.08, steel))
        parts.append(span(f"cleat_horn_{y}", OUTER - 0.31, OUTER - 0.21, y - 0.2, y + 0.2, DECK + 0.08, DECK + 0.13, steel))
    parts.append(cyl("rope_coil", 0.18, 0.06, (OUTER - 0.55, 2.8, DECK + 0.03), mat("rope", 0.95), verts=12))
    # two short timber bollards near the ends, capped in steel
    for y in (-3.9, 3.9):
        parts.append(cyl(f"bollard_{y}", 0.13, 0.5, (OUTER - 0.3, y, DECK + 0.25), pile, verts=10))
        parts.append(cyl(f"bollard_cap_{y}", 0.15, 0.06, (OUTER - 0.3, y, DECK + 0.53), steel, verts=10))
    # fenders hanging on the boat side
    for y in (-2.4, 0.0, 2.4):
        parts.append(cyl(f"fender_{y}", 0.11, 0.6, (OUTER + 0.17, y, DECK - 0.25), fender, verts=10))
        parts.append(span(f"fender_tie_{y}", OUTER + 0.02, OUTER + 0.18, y - 0.01, y + 0.01, DECK + 0.03, DECK + 0.08, mat("rope", 0.95)))
    # guide piles just off the pontoon's ends, in steel hoops: they hold it to the wall and stand above the quay
    for s in (-1, 1):
        py = s * (LENGTH / 2 + 0.28)
        px = OUTER - 0.45
        parts.append(cyl(f"pile_{s}", 0.17, 7.6, (px, py, -3.2), pile, verts=12))
        parts.append(cyl(f"pile_top_{s}", 0.19, 0.08, (px, py, 0.62), steel, verts=12))
        parts.append(cyl(f"hoop_{s}", 0.24, 0.12, (px, py, DECK - 0.05), steel, verts=12))
        parts.append(span(f"hoop_arm_{s}", px - 0.06, px + 0.06, min(py, s * LENGTH / 2), max(py, s * LENGTH / 2), DECK - 0.1, DECK, steel))


def stair(parts):
    plank, plank2, timber = mat("plank"), mat("plank2"), mat("timber")
    # stringers along the slope, either side
    top = (HEAD - 0.15, 0.0)
    bot = (FOOT + 0.05, DECK)
    for x in (X0 + 0.03, X1 - 0.03):
        parts.append(beam(f"stringer_{x:.2f}", (x, top[0], top[1] - 0.12), (x, bot[0], bot[1] - 0.05), 0.06, 0.26, timber))
    # treads (19; the 20th riser lands on the deck)
    for k in range(1, RISERS):
        ya, yb = HEAD + (k - 1) * TREAD, HEAD + k * TREAD
        z = -k * RISER
        parts.append(span(f"tread_{k}", X0 + 0.06, X1 - 0.06, ya - 0.02, yb, z - 0.05, z, plank2 if k % 4 == 2 else plank))
    # the landing on the quay top, its joists, and brackets to the wall under its overhang
    parts.append(span("landing", LANDING_IN, X1 + 0.04, HEAD - LANDING_D, HEAD, LANDING_TOP - 0.06, LANDING_TOP, plank))
    for i in range(5):
        y = HEAD - LANDING_D + 0.1 + i * (LANDING_D - 0.2) / 4
        parts.append(span(f"landing_seam_{i}", LANDING_IN, X1 + 0.04, y - 0.006, y + 0.006, LANDING_TOP, LANDING_TOP + 0.004, plank2))
    parts.append(span("landing_joist", 0.0, X1 + 0.04, HEAD - LANDING_D, HEAD, LANDING_TOP - 0.2, LANDING_TOP - 0.06, timber))
    for y in (HEAD - LANDING_D + 0.1, HEAD - 0.1):
        parts.append(beam(f"landing_strut_{y:.1f}", (0.0, y, -1.0), (X1, y, -0.15), 0.08, 0.1, timber))
    # wall brackets under the stair's open stringer
    for y in (HEAD + 1.4, HEAD + 3.0):
        z = nosing(y) - 0.3
        parts.append(span(f"bracket_{y:.1f}", 0.0, X1, y - 0.05, y + 0.05, z - 0.12, z, timber))
        parts.append(beam(f"brace_{y:.1f}", (0.0, y, z - 0.9), (X1 - 0.05, y, z - 0.05), 0.08, 0.08, timber))
    # handrails: posts every ~1.2 m along each side, a top rail at RAIL_H over the nosings and a mid rail
    for x in (X0 - 0.04, X1 + 0.04):
        ys = [HEAD + 0.1 + i * (FOOT - 0.25 - HEAD) / 4 for i in range(5)]
        for y in ys:
            zb = nosing(y) - 0.05
            parts.append(span(f"post_{x:.2f}_{y:.1f}", x - 0.04, x + 0.04, y - 0.04, y + 0.04, zb, zb + RAIL_H + 0.05, timber))
        parts.append(beam(f"handrail_{x:.2f}", (x, HEAD, RAIL_H + 0.02), (x, FOOT, DECK + RAIL_H + 0.02 - RISER / 2), 0.08, 0.06, timber))
        parts.append(beam(f"midrail_{x:.2f}", (x, HEAD + 0.1, RAIL_H / 2), (x, FOOT - 0.2, DECK + RAIL_H / 2 + 0.2 * RISER / TREAD), 0.05, 0.05, timber))
    # the landing's rails: along its open side and across its far end, posts at the corners
    xo, yf = X1 + 0.04, HEAD - LANDING_D + 0.04
    for (x, y) in ((xo, yf), (xo, HEAD - 0.04), (X0 - 0.04, yf), (-0.3, yf)):
        parts.append(span(f"lpost_{x:.2f}_{y:.2f}", x - 0.045, x + 0.045, y - 0.045, y + 0.045, LANDING_TOP, LANDING_TOP + RAIL_H + 0.05, timber))
    parts.append(span("lrail_side", xo - 0.04, xo + 0.04, yf, HEAD, LANDING_TOP + RAIL_H - 0.06, LANDING_TOP + RAIL_H, timber))
    parts.append(span("lrail_end", -0.3, xo + 0.04, yf - 0.04, yf + 0.04, LANDING_TOP + RAIL_H - 0.06, LANDING_TOP + RAIL_H, timber))
    parts.append(span("lmid_side", xo - 0.025, xo + 0.025, yf, HEAD, LANDING_TOP + 0.45, LANDING_TOP + 0.5, timber))
    parts.append(span("lmid_end", -0.3, xo, yf - 0.025, yf + 0.025, LANDING_TOP + 0.45, LANDING_TOP + 0.5, timber))


def build():
    reset()
    parts = []
    pontoon(parts)
    stair(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    pier = bpy.context.active_object
    pier.name = "Pier"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return pier


def export_glb(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "pier.glb"), use_selection=True, export_format="GLB")


def export_json(obj):
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
            u = (co.x, co.z, co.y)
            for i in range(3):
                lo[i], hi[i] = min(lo[i], u[i]), max(hi[i], u[i])
            part["v"] += [round(co.x, 5), round(co.z, 5), round(co.y, 5)]
            nn = face.normal if not face.smooth else loop.vert.normal
            part["n"] += [round(nn.x, 4), round(nn.z, 4), round(nn.y, 4)]
        part["t"] += [base, base + 2, base + 1]
    bm.free()
    size = [round(hi[i] - lo[i], 3) for i in range(3)]
    data = {"name": "pier", "size": size, "parts": list(parts.values())}
    with open(os.path.join(OUT, "pier.mesh.json"), "w") as f:
        json.dump(data, f, separators=(",", ":"))
    tris = sum(len(p["t"]) // 3 for p in parts.values())
    print(f"pier: {len(parts)} parts, {tris} triangles, size {size}, slope {math.degrees(math.atan2(RISER, TREAD)):.1f} deg, riser {RISER:.3f} m")


def render_scene():
    """Render-only context: the quay (top at 0, a timber-piled wall face at x = 0), the water, and the speedboat alongside."""
    def m(name, rgb):
        mm = bpy.data.materials.new(name)
        mm.use_nodes = True
        mm.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (*rgb, 1)
        return mm
    stone, wallp, water = m("quay", (0.45, 0.45, 0.43)), m("wallpile", (0.25, 0.21, 0.17)), m("water", (0.12, 0.25, 0.32))
    span("quay", -10, 0, -14, 14, -9, 0, stone)
    for i in range(-35, 36):
        cyl(f"wallpile_{i}", 0.17, 9.2, (0.05, i * 0.4, -4.6), wallp, verts=8)
    span("capping", -0.3, 0.25, -14, 14, -0.18, 0.02, wallp)
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, WATER))
    bpy.context.active_object.data.materials.append(water)
    boat = os.path.join(OUT, "speedboat.glb")
    if os.path.exists(boat):
        bpy.ops.import_scene.gltf(filepath=boat)
        for o in bpy.context.selected_objects:
            if o.parent is None:
                o.location = (OUTER + 0.25 + 1.15, 0.0, WATER)
    else:
        print("no out/speedboat.glb: rendering without the boat (run build_speedboat.py first)")


def render(name, cam_loc, target, lens=35):
    scene = bpy.context.scene
    bpy.ops.object.camera_add(location=cam_loc)
    cam = bpy.context.active_object
    cam.data.lens = lens
    d = Vector(target) - Vector(cam_loc)
    cam.rotation_euler = (math.atan2(math.hypot(d.x, d.y), -d.z), 0, math.atan2(d.y, d.x) - math.pi / 2)
    scene.camera = cam
    scene.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)


def render_previews():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "CYCLES"
    scene.render.resolution_x, scene.render.resolution_y = 1400, 900
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.62, 0.70, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    scene.world = world
    bpy.ops.object.light_add(type="SUN", location=(4, -4, 8))
    sun = bpy.context.active_object
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(-60))
    render_scene()
    render("pier.png", (13.0, -9.0, 3.5), (1.2, -0.5, -2.6))
    render("pier_stair.png", (2.0, 4.1, DECK + 1.65), (0.6, -4.6, -1.4), lens=20)


pier = build()
export_glb(pier)
export_json(pier)
if RENDER:
    render_previews()
