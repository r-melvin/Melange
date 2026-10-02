"""Pirate accessories for an NPC (smuggling spoke): a tricorne hat and an eyepatch. Low poly.

tricorn: a black felt tricorne: a slightly domed crown on a wide brim cocked up on three sides, so from above the brim is a
triangle with one point at the front (+Y) and two at the back corners, with gold braid along the brim's upper edge and a small
white skull badge on the front point. About 0.40 W x 0.16 H x 0.38 D.
  Origin: centre of the bottom of the crown opening (where the hat sits on the head); the head's top pokes about 0.06 m up into
  the crown.

eyepatch: a black oval patch, slightly curved, over the character's LEFT eye (-X), on a thin strap that loops round the head,
level with the patch at the front and tilted up across the right side (above the right ear) and round the back.
  Origin: bridge of the nose at eye height, on the face surface. Patch centre at about (-0.033, +0.006, 0) Blender.

Run:  blender -b --python scripts/art/build_pirate.py -- [--out DIR] [--render] [--only tricorn]
Writes, per model: <name>.glb, <name>.mesh.json (one part per colour, Unity axes: x right, y up, z towards the front;
left-handed winding) and <name>.png with --render. The renders include a grey placeholder head, which is not exported.

Conventions: metres; Blender Z up, +Y is the front, +X is the character's right. Same pipeline as build_gunrack.py (primitives,
bmesh, join, export); colours are lighter than they look in Blender because in-game lighting is darker.
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
    "felt": (0.20, 0.19, 0.19),        # black felt, lightened
    "gold": (0.88, 0.72, 0.32),        # braid trim
    "bone": (0.92, 0.91, 0.86),        # skull badge
    "patch": (0.12, 0.11, 0.11),
    "strap": (0.15, 0.15, 0.15),
}
METAL = ("gold",)

# tricorn
CROWN_RX, CROWN_RY = 0.105, 0.112      # inner radii at the opening
CROWN_T = 0.006                        # felt thickness of the crown
CROWN_H = 0.125                        # top of the crown above the opening
BRIM_T = 0.010
FRONT = math.pi / 2                    # the brim's front point, at +Y
# eyepatch: the placeholder head the strap and patch are fitted to (Blender axes)
HEAD_C, HEAD_R = Vector((0.0, -0.095, 0.02)), Vector((0.095, 0.105, 0.115))


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


def blob(name, size, loc, m, rot=(0, 0, 0), segs=8, rings=5):
    """A low-poly ellipsoid; size is the full (x, y, z) extent."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=0.5, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(m)
    return o


def sheet(name, grid, mats, seg_mat, closed_v=False, caps=()):
    """A mesh from a grid of points grid[i][j]: i runs round a closed loop, j along a profile (closed if closed_v).
    seg_mat[j] is the material index of the band between profile points j and j + 1. caps: (point, j, material index) fans
    that close the profile's open end at row j onto a single apex point."""
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for m in mats:
        obj.data.materials.append(m)
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in row] for row in grid]
    n, k = len(vs), len(vs[0])
    for i in range(n):
        a, b = vs[i], vs[(i + 1) % n]
        for j in range(k if closed_v else k - 1):
            j2 = (j + 1) % k
            f = bm.faces.new((a[j], b[j], b[j2], a[j2]))
            f.material_index = seg_mat[j]
    for apex, j, mi in caps:
        c = bm.verts.new(apex)
        for i in range(n):
            f = bm.faces.new((vs[i][j], c, vs[(i + 1) % n][j]))
            f.material_index = mi
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    return obj


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


# ---------------------------------------------------------------------------------------------------------------- tricorn

def crown_r(th, extra=0.0):
    """Radius of the crown's elliptical opening at angle th (from +X, counter-clockwise seen from above)."""
    a, b = CROWN_RX + extra, CROWN_RY + extra
    return a * b / math.hypot(b * math.cos(th), a * math.sin(th))


def corner_phi(th):
    """Angle from the nearest brim corner (corners at the front and the two back corners, 120 degrees apart), -60..60 deg."""
    return (th - FRONT + math.pi / 3) % (2 * math.pi / 3) - math.pi / 3


def brim_profile(th):
    """The brim's closed cross-section at angle th, as 3D points: upper surface from the crown out and up the wall to the lip,
    then back down the outside of the wall and along the underside."""
    c, s = math.cos(th), math.sin(th)
    side = math.pi / 3 - abs(corner_phi(th))       # angle from the middle of the nearest side, 0..60 deg
    corner = side / (math.pi / 3)                  # 0 mid-side .. 1 at a corner
    rin = crown_r(th, CROWN_T) - 0.004             # buried in the crown wall
    rf = max(0.122 / max(math.cos(side), 0.57), rin + 0.015)    # the fold line: a triangle, corners cut round at 0.214
    drop = -0.03 * corner ** 2                     # the corners droop
    h = 0.045 + 0.05 * math.cos(1.5 * side)        # wall height above the fold
    flare = 0.014 + 0.01 * (1 - corner)
    top = [(rin, BRIM_T), (rf - 0.012, drop * 0.7 + BRIM_T), (rf, drop + BRIM_T + 0.012),
           (rf + flare * 0.9, drop + h - 0.012), (rf + flare, drop + h)]
    bot = [(rf + flare + BRIM_T, drop + h), (rf + flare * 0.9 + BRIM_T, drop + h - 0.012),
           (rf + BRIM_T * 0.9, drop + 0.006), (rf - 0.012, drop * 0.7), (rin, 0.0)]
    return [(r * c, r * s, z) for r, z in top + bot]


def build_tricorn():
    reset()
    felt, gold, bone = mat("felt", 0.95), mat("gold", 0.45, 0.6), mat("bone", 0.8)
    FELT, GOLD = 0, 1
    N = 36                                         # every 10 degrees, so the three corners are sampled exactly
    # the crown: inner surface (up from the opening) and outer surface, both domed; closed at the rim and by apex fans
    prof = [(0.0, 1.0), (0.05, 1.02), (0.09, 0.97), (0.11, 0.84), (CROWN_H - 0.004, 0.5)]     # (z, radius scale)
    grid = []
    for i in range(N):
        th = 2 * math.pi * i / N
        c, s = math.cos(th), math.sin(th)
        row = []
        for z, k in reversed(prof[:-1]):          # inner, top down; it stops short of the outer top
            r = crown_r(th) * k
            row.append((r * c, r * s, min(z, CROWN_H - CROWN_T - 0.012)))
        for z, k in prof:                          # outer, bottom up
            r = crown_r(th, CROWN_T) * k
            row.append((r * c, r * s, z))
        grid.append(row)
    k = len(grid[0])
    crown = sheet("crown", grid, [felt], [FELT] * k, caps=[((0, 0, CROWN_H), k - 1, FELT),
                                                            ((0, 0, CROWN_H - CROWN_T - 0.004), 0, FELT)])
    # the brim: a closed, 1 cm thick profile swept round; flat by the crown, then turned up into walls that follow a rounded
    # triangle, high in the middle of each side and low at the corners, which droop a little
    grid = [brim_profile(2 * math.pi * i / N) for i in range(N)]
    #       inner flat, bend, wall, braid(in), lip, braid(out), outer wall, under bend, under flat, inner edge
    seg = [FELT, FELT, FELT, GOLD, GOLD, GOLD, FELT, FELT, FELT, FELT]
    brim = sheet("brim", grid, [felt, gold], seg, closed_v=True)
    parts = [crown, brim]
    # a small skull badge on the outside of the front point's wall, leaning with it
    p = brim_profile(FRONT)
    lo, hi = Vector(p[7]), Vector(p[6])            # outer wall, bottom and top of its straight part
    lean = math.atan2(hi.y - lo.y, hi.z - lo.z)
    c = lo.lerp(hi, 0.6) + Vector((0, math.cos(lean), -math.sin(lean))) * 0.003
    rot = (-lean, 0, 0)
    up = Vector((0, math.sin(lean), math.cos(lean)))
    parts.append(blob("skull", (0.026, 0.009, 0.021), c + up * 0.003, bone, rot=rot))
    parts.append(blob("jaw", (0.015, 0.008, 0.010), c - up * 0.007, bone, rot=rot, segs=6, rings=4))
    return join(parts, "tricorn")


# --------------------------------------------------------------------------------------------------------------- eyepatch

def head_front_y(x, z, clear=0.0):
    """y of the placeholder head's front surface at (x, z), plus clear."""
    u = 1 - (x / HEAD_R.x) ** 2 - ((z - HEAD_C.z) / HEAD_R.z) ** 2
    return HEAD_C.y + HEAD_R.y * math.sqrt(max(u, 0.0)) + clear


def head_scale(p):
    """How far p lies out from the head's centre, in head radii (1 = on the surface)."""
    d = p - HEAD_C
    return math.sqrt((d.x / HEAD_R.x) ** 2 + (d.y / HEAD_R.y) ** 2 + (d.z / HEAD_R.z) ** 2)


def build_eyepatch():
    reset()
    patch, strap = mat("patch", 0.85), mat("strap", 0.8)
    # the patch: an oval that follows the face, 6 mm thick in the middle, thinning to 3 mm at the edge
    px, pw, ph = -0.033, 0.048, 0.040
    N = 16
    rings = [(1.0, 0.003), (0.6, 0.0055), (0.0, 0.006)]       # (radius fraction, thickness)
    grid = []
    for i in range(N):
        a = 2 * math.pi * i / N
        row = []
        for f, t in rings:                                  # back, edge in
            x, z = px + f * pw / 2 * math.cos(a), f * ph / 2 * math.sin(a)
            row.append((x, head_front_y(x, z, 0.003), z))
        row = list(reversed(row))                           # back: centre out to the edge
        front = []                                          # front: edge in to the centre
        for f, t in rings:
            x, z = px + f * pw / 2 * math.cos(a), f * ph / 2 * math.sin(a)
            front.append((x, head_front_y(x, z, 0.003) + t, z))
        grid.append(row + front)
    k = len(grid[0])
    back_c = (px, head_front_y(px, 0, 0.003), 0.0)
    front_c = (px, head_front_y(px, 0, 0.003) + 0.006, 0.0)
    # profile: back centre ring (f=0) .. back edge, front edge .. front centre ring; the f=0 rings collapse to the centre,
    # so drop them and fan instead
    grid = [row[1:-1] for row in grid]
    k = len(grid[0])
    pobj = sheet("patch", grid, [patch], [0] * k, caps=[(back_c, 0, 0), (front_c, k - 1, 0)])
    # the strap: an ellipse round the head, level with the patch on the left and tilted up (about the Y axis, from the patch
    # centre) across the right side, so it crosses the forehead over the right eye and passes above the right ear; then moved onto the placeholder head's surface (radially from its centre)
    cy, ax, ay = -0.095, 0.098, 0.102
    tilt = math.radians(20)
    sw, st = 0.008, 0.003
    M = 48
    grid = []
    for i in range(M):
        a = 2 * math.pi * i / M
        x, y = ax * math.cos(a), cy + ay * math.sin(a)
        d = x - px                                        # level across the patch, rising to the right of it
        z = math.tan(tilt) * 0.5 * (d + math.sqrt(d * d + 0.012 ** 2))
        p = Vector((x, y, z))
        out = Vector((math.cos(a) / ax, math.sin(a) / ay, 0)).normalized()      # outward in the strap's plane
        up = Vector((-math.sin(tilt), 0, math.cos(tilt)))
        s = head_scale(p)
        need = 1.0 + (st / 2 + 0.001) / min(HEAD_R)       # strap's inner face just clear of the head
        p = HEAD_C + (p - HEAD_C) * (need / s)            # so it hugs the head rather than standing off it
        row = [p - out * st / 2 - up * sw / 2, p + out * st / 2 - up * sw / 2, p + out * st / 2 + up * sw / 2,
               p - out * st / 2 + up * sw / 2]
        grid.append(row)
    sobj = sheet("strap", grid, [strap], [0] * 4, closed_v=True)
    return join([pobj, sobj], "eyepatch")


# ----------------------------------------------------------------------------------------------------------------- export

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
    print(f"{name}: {len(parts)} parts, {tris} triangles, size {size}, "
          f"min {[round(v, 3) for v in lo]}, max {[round(v, 3) for v in hi]}")


def render_preview(obj, name, head, reach=2.0, view=(0.55, 1.0, 0.6), target=(0, 0, 0)):
    """A 3/4 view from the front right with a grey placeholder head (centre, half-sizes; render only, never exported); reach is
    the camera distance in multiples of the model's largest dimension."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "CYCLES"
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.55, 0.58, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    scene.world = world
    grey = bpy.data.materials.new("head")
    grey.use_nodes = True
    grey.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.62, 0.62, 0.62, 1)
    grey.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = 0.7
    centre, half = head
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=1.0, location=centre)
    h = bpy.context.active_object
    h.scale = half
    h.data.materials.append(grey)
    bpy.ops.object.shade_smooth()
    bpy.ops.object.light_add(type="SUN", location=(3, 4, 6))
    sun = bpy.context.active_object
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(150))
    dims = obj.dimensions
    target = Vector(target)
    cam_loc = target + Vector(view).normalized() * max(dims.x, dims.y, dims.z) * reach
    bpy.ops.object.camera_add(location=cam_loc)
    cam = bpy.context.active_object
    cam.rotation_euler = (target - cam_loc).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_start = 0.01
    scene.camera = cam
    scene.render.filepath = os.path.join(OUT, f"{name}.png")
    bpy.ops.render.render(write_still=True)


MODELS = {
    "tricorn": (build_tricorn, dict(head=((0, 0, -0.05), (0.095, 0.105, 0.115)), reach=1.75, view=(0.4, 1.0, 0.6),
                                    target=(0, 0.02, -0.01))),
    "eyepatch": (build_eyepatch, dict(head=(tuple(HEAD_C), tuple(HEAD_R)), reach=1.7, view=(-0.45, 1.0, 0.3),
                                      target=(0, -0.06, 0.02))),
}
for model, (fn, view) in MODELS.items():
    if ONLY and model != ONLY:
        continue
    o = fn()
    export_glb(o, model)
    export_json(o, model)
    if RENDER:
        render_preview(o, model, **view)
