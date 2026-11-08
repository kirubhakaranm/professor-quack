"""Turn an AI-generated Quacks GLB into a game-ready, rigged FBX for Unity.

Steps: import -> drop tiny loose fragments -> decimate to a triangle budget -> face the front
along Unity +Z, feet at origin, fixed height -> add a minimal armature (root, body, neck/head,
jaw, wing_L, wing_R) placed from proportions -> automatic weights, with the lower beak bound
fully to the jaw -> export FBX (textures embedded) + preview renders.

Usage:
  Blender -b --python tools/blender/prepare_quacks.py -- <in.glb> <out.fbx> [--front -Y|+Y|-X|+X]
          [--tris 40000] [--height 1.2] [--joints joints.json] [--preview out_prefix]

Joint positions are fractions of the model's bounding box and are printed; override with a JSON
file if a model's proportions differ. Generated meshes are fused (one skin), so the jaw and wings
deform approximately — this is expected, not a bug.
"""
import bpy, bmesh, sys, json, math, mathutils
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[0], args[1]
opt = {"--front": "-Y", "--tris": "40000", "--height": "1.2", "--joints": None, "--preview": None, "--strip-head": None, "--face": None}
for i, a in enumerate(args):
    if a in ("--strip-head", "--face"): opt[a] = "1"
    elif a in opt and i + 1 < len(args): opt[a] = args[i + 1]
TRIS, HEIGHT = int(opt["--tris"]), float(opt["--height"])

# fractions: x across (width), y forward (depth, + = front), z up (height)
J = {
    "body":     (0.00, 0.00, 0.30),
    "neck":     (0.00, 0.00, 0.60),
    "head":     (0.00, 0.05, 0.74),
    "head_top": (0.00, 0.05, 1.00),
    "jaw":      (0.00, 0.22, 0.70),   # hinge near the beak root
    "jaw_tip":  (0.00, 0.50, 0.68),
    "wing_L":   (0.36, 0.00, 0.52),
    "wing_L_tip": (0.42, 0.00, 0.30),
    "wing_R":   (-0.36, 0.00, 0.52),
    "wing_R_tip": (-0.42, 0.00, 0.30),
}
if opt["--joints"]:
    J.update({k: tuple(v) for k, v in json.load(open(opt["--joints"])).items()})

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "QuacksMesh"
# clear parents/transforms into the mesh
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o.type == 'EMPTY': bpy.data.objects.remove(o)
tris0 = sum(len(p.vertices) - 2 for p in body.data.polygons)

# 1) weld texture-seam splits (generated meshes are cut into UV charts), then remove small debris
bm = bmesh.new(); bm.from_mesh(body.data)
diag = max(body.dimensions) or 1.0
nv0 = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=diag * 1e-5)
print(f"QUACKS welded seams: {nv0} -> {len(bm.verts)} vertices")
bm.verts.ensure_lookup_table()
seen, islands = set(), []
for v in bm.verts:
    if v.index in seen: continue
    comp, stack = [], [v]
    while stack:
        x = stack.pop()
        if x.index in seen: continue
        seen.add(x.index); comp.append(x); stack.extend(e.other_vert(x) for e in x.link_edges)
    islands.append(comp)
keep_min = max(30, int(0.0005 * len(bm.verts)))   # only true debris
removed = [c for c in islands if len(c) < keep_min]
bmesh.ops.delete(bm, geom=[v for c in removed for v in c], context='VERTS')
bm.to_mesh(body.data); bm.free()
print(f"QUACKS fragments: {len(islands)} islands, removed {len(removed)} small ones")

# 2) decimate to budget
tris1 = sum(len(p.vertices) - 2 for p in body.data.polygons)
if tris1 > TRIS:
    m = body.modifiers.new("decimate", 'DECIMATE'); m.ratio = TRIS / tris1
    bpy.ops.object.modifier_apply(modifier=m.name)
tris2 = sum(len(p.vertices) - 2 for p in body.data.polygons)
print(f"QUACKS triangles: {tris0} -> {tris1} (after cleanup) -> {tris2}")

# 3) orient: model front -> Blender -Y (which the FBX exporter maps to Unity +Z), feet at 0, height fixed
rot = {"-Y": 0, "+X": -90, "+Y": 180, "-X": 90}[opt["--front"]]   # brings the front to -Y
# rotate mesh data directly (operator context is unreliable in background mode)
body.data.transform(mathutils.Matrix.Rotation(math.radians(rot), 4, 'Z')); body.data.update()
print(f"QUACKS front {opt['--front']} -> rotated {rot} deg about Z")
bb = [v.co.copy() for v in body.data.vertices]   # bound_box can be stale after data edits
mn = Vector([min(v[i] for v in bb) for i in range(3)]); mx = Vector([max(v[i] for v in bb) for i in range(3)])
s = HEIGHT / (mx.z - mn.z)
body.data.transform(mathutils.Matrix.Scale(s, 4)); body.data.update()
bb = [v.co.copy() for v in body.data.vertices]   # bound_box can be stale after data edits
mn = Vector([min(v[i] for v in bb) for i in range(3)]); mx = Vector([max(v[i] for v in bb) for i in range(3)])
ctr = (mn + mx) / 2
for v in body.data.vertices: v.co -= Vector((ctr.x, ctr.y, mn.z))
W, D, H = mx.x - mn.x, mx.y - mn.y, mx.z - mn.z
def P(name):  # fraction -> position; forward is -Y in Blender
    fx, fy, fz = J[name]; return Vector((fx * W, -fy * D, fz * H))
print("QUACKS size (m): width %.3f depth %.3f height %.3f" % (W, D, H))

# 3a) --face: find face parts from the base-colour texture (full generated head kept):
#     lower beak (orange, below the mouth line), bow tie (burgundy), eye whites, brows (dark brown).
#     Paints the baked brows out of the texture (Unity adds moving brows) and writes <name>_face.json.
import numpy as np, colorsys, json, os
FACE = {}
masks = {}
if opt["--face"]:
    me = body.data
    img = None
    for mat in me.materials:
        if mat and mat.use_nodes:
            for n in mat.node_tree.nodes:
                if n.type == 'BSDF_PRINCIPLED' and n.inputs['Base Color'].is_linked and n.inputs['Base Color'].links[0].from_node.type == 'TEX_IMAGE':
                    img = n.inputs['Base Color'].links[0].from_node.image
    TW, TH = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(TH, TW, 4)
    uvl = me.uv_layers.active.data
    nv = len(me.vertices)
    vuv = np.zeros((nv, 2)); cnt = np.zeros(nv)
    for p in me.polygons:
        for li in p.loop_indices:
            vi = me.loops[li].vertex_index; vuv[vi] += uvl[li].uv; cnt[vi] += 1
    vuv /= np.maximum(cnt, 1)[:, None]
    col = px[np.clip((vuv[:, 1] * TH).astype(int), 0, TH - 1), np.clip((vuv[:, 0] * TW).astype(int), 0, TW - 1), :3]
    hsv = np.array([colorsys.rgb_to_hsv(*c) for c in col]); h_, s_, v_ = hsv[:, 0], hsv[:, 1], hsv[:, 2]
    co = np.array([v.co[:] for v in me.vertices])
    zf = co[:, 2] / H                                  # feet at 0
    yr = (co[:, 1] - co[:, 1].min()) / D               # 0 = front-most (front is -Y)
    masks["jaw"] = (h_ > 0.02) & (h_ < 0.075) & (s_ > 0.65) & (v_ > 0.6) & (zf > 0.6) & (zf < 0.82) & (yr < 0.22)
    masks["tie"] = ((h_ < 0.02) | (h_ > 0.93)) & (s_ > 0.45) & (v_ > 0.15) & (v_ < 0.75) & (zf > 0.42) & (zf < 0.58) & (yr < 0.36)
    eye = (s_ < 0.18) & (v_ > 0.78) & (zf > 0.7) & (zf < 0.83) & (yr < 0.37)
    brow = (h_ < 0.1) & (s_ > 0.35) & (v_ < 0.5) & (v_ > 0.1) & (zf > 0.82) & (yr < 0.37)
    for k, m in masks.items(): print(f"QUACKS face mask {k}: {int(m.sum())} vertices")
    def side(m, sgn): return m & (np.sign(co[:, 0]) == sgn)
    for name, m in (("eye", eye), ("brow", brow)):
        for sgn, tag in ((1, "L"), (-1, "R")):   # Blender +X = his left
            mm = side(m, sgn)
            if mm.sum() < 3: continue
            c = co[mm]; ctr = c.mean(0)
            FACE[f"{name}{tag}"] = [round(float(x), 4) for x in ctr]
            if name == "eye": FACE[f"eye{tag}_r"] = round(float(np.percentile(np.linalg.norm((c - ctr)[:, [0, 2]], axis=1), 95)), 4)
            else: FACE[f"brow{tag}_half"] = round(float((c[:, 0].max() - c[:, 0].min()) / 2), 4)
    j = co[masks["jaw"]]
    if len(j):
        FACE["jaw_hinge"] = [0.0, round(float(j[:, 1].max()), 4), round(float(np.percentile(j[:, 2], 85)), 4)]
        FACE["jaw_tip"] = [0.0, round(float(j[:, 1].min()), 4), round(float(np.percentile(j[:, 2], 30)), 4)]
    t = co[masks["tie"]]
    if len(t): FACE["tie"] = [round(float(x), 4) for x in t.mean(0)]
    FACE["height"] = H
    print("QUACKS face metrics:", FACE)

    # paint the baked brows out: inside brow-zone faces, brown pixels -> surrounding feather yellow
    feather = np.median(col[(zf > 0.78) & (h_ > 0.09) & (h_ < 0.16) & (s_ > 0.5) & (v_ > 0.6)], axis=0)
    zone = (zf > 0.8) & (zf < 0.92) & (yr < 0.4)
    painted = 0
    for p in me.polygons:
        vs = list(p.vertices)
        if not zone[vs].all(): continue
        uvs = np.array([uvl[li].uv[:] for li in p.loop_indices]) * [TW, TH]
        x0, y0 = np.floor(uvs.min(0)).astype(int); x1, y1 = np.ceil(uvs.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, TW - 1), min(y1, TH - 1)
        if x1 <= x0 or y1 <= y0: continue
        patch = px[y0:y1 + 1, x0:x1 + 1, :3]
        ph = np.array([colorsys.rgb_to_hsv(*c) for c in patch.reshape(-1, 3)]).reshape(patch.shape)
        bad = (ph[..., 0] < 0.11) & (ph[..., 1] > 0.3) & (ph[..., 2] < 0.55)
        if bad.any():
            patch[bad] = feather; painted += int(bad.sum())
    if painted:
        img.pixels[:] = px.ravel().tolist(); img.update()
    print(f"QUACKS painted out baked brows: {painted} pixels")

    # paint the baked pupils/irises out too (Unity adds moving eyes): inside each eye's sclera disc, dark or brown
    # pixels -> the sclera's own white. Otherwise the old pupil peeks out beside the animated eye when the head turns.
    sclera = np.median(col[eye], axis=0) if eye.sum() > 10 else np.array([0.93, 0.92, 0.9])
    painted_eyes = 0
    for tag in ("L", "R"):
        if f"eye{tag}" not in FACE: continue
        ec = np.array(FACE[f"eye{tag}"]); er = FACE[f"eye{tag}_r"]
        dxz = np.linalg.norm((co - ec)[:, [0, 2]], axis=1)
        inside = (dxz < er * 1.12) & (co[:, 1] < ec[1] + 0.04 * D)
        for p in me.polygons:
            vs = list(p.vertices)
            if not inside[vs].all(): continue
            uvs = np.array([uvl[li].uv[:] for li in p.loop_indices]) * [TW, TH]
            x0, y0 = np.floor(uvs.min(0)).astype(int); x1, y1 = np.ceil(uvs.max(0)).astype(int)
            x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, TW - 1), min(y1, TH - 1)
            if x1 <= x0 or y1 <= y0: continue
            patch = px[y0:y1 + 1, x0:x1 + 1, :3]
            ph = np.array([colorsys.rgb_to_hsv(*c) for c in patch.reshape(-1, 3)]).reshape(patch.shape)
            # centre (old pupil + iris): dark or brown -> white. Outside it the black glasses rim can sit inside the
            # disc, so there only mid-tone brown iris pixels go, never near-black ones (the rim) and never greys.
            centre = dxz[vs].mean() < er * 0.85
            if not centre and co[vs, 2].mean() < ec[2] - er * 0.5: continue   # outer ring below the eye = the beak's nostrils
            brownish = (ph[..., 1] > 0.35) & (ph[..., 0] < 0.15)
            bad = ((ph[..., 2] < 0.6) | brownish) if centre else (brownish & (ph[..., 2] > 0.24) & (ph[..., 2] < 0.75))
            if bad.any():
                patch[bad] = sclera; painted_eyes += int(bad.sum())
    if painted_eyes:
        img.pixels[:] = px.ravel().tolist(); img.update()
    print(f"QUACKS painted out baked pupils: {painted_eyes} pixels")

# 3b) optional: remove the generated head (baked eyes/glasses can't animate); Unity attaches the
#     procedural Design A head + controllable face to the head bone instead.
if opt["--strip-head"]:
    cut = P("neck").z + 0.015 * H
    bm = bmesh.new(); bm.from_mesh(body.data)
    gone = [v for v in bm.verts if v.co.z > cut]
    bmesh.ops.delete(bm, geom=gone, context='VERTS')
    bm.to_mesh(body.data); bm.free()
    print(f"QUACKS stripped head above z={cut:.3f} m ({len(gone)} vertices)")

# 4) armature
arm_data = bpy.data.armatures.new("QuacksRig"); arm = bpy.data.objects.new("QuacksRig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
def bone(name, head, tail, parent=None):
    b = eb.new(name); b.head = head; b.tail = tail
    if parent: b.parent = eb[parent]
    return b
bone("root", Vector((0, 0, 0)), P("body"))
bone("body", P("body"), P("neck"), "root")
bone("head", P("neck"), P("head_top"), "body")
if "jaw_hinge" in FACE: bone("jaw", Vector(FACE["jaw_hinge"]), Vector(FACE["jaw_tip"]), "head")
else: bone("jaw", P("jaw"), P("jaw_tip"), "head")
if "tie" in FACE: bone("tie", Vector(FACE["tie"]), Vector(FACE["tie"]) + Vector((0, -0.05, 0)), "body")
bone("wing_L", P("wing_L"), P("wing_L_tip"), "body")
bone("wing_R", P("wing_R"), P("wing_R_tip"), "body")
# Option A: rigid feet and a tail, found from the mesh itself (feet = lowest slab, tail = back-most tuft).
#   Feet pivot at the ankle so a step lifts the whole webbed foot; the tail pivots where it meets the body.
_co = np.array([v.co[:] for v in body.data.vertices])
_lsign = 1.0 if P("wing_L").x > 0 else -1.0
FEET = {}
for nm, sg in (("foot_L", _lsign), ("foot_R", -_lsign)):
    m = (_co[:, 2] < 0.07 * H) & (np.sign(_co[:, 0]) == sg)
    if m.sum() > 30:
        fx, fy = float(np.median(_co[m, 0])), float(np.median(_co[m, 1]))
        FEET[nm] = (fx, fy)
        bone(nm, Vector((fx, fy + 0.02 * D, 0.075 * H)), Vector((fx, fy - 0.10 * D, 0.02 * H)), "root")
_ymin = _co[:, 1].min()
_tm = (_co[:, 1] > _ymin + 0.86 * D) & (_co[:, 2] > 0.18 * H) & (_co[:, 2] < 0.6 * H) & (np.abs(_co[:, 0]) < 0.3 * W)
TAIL = None
if _tm.sum() > 30:
    c = _co[_tm].mean(axis=0); TAIL = (float(c[1]), float(c[2]))
    bone("tail", Vector((0, c[1] - 0.06 * D, c[2] - 0.02 * H)), Vector((0, c[1] + 0.06 * D, c[2] + 0.04 * H)), "body")
print("QUACKS option A: feet", {k: tuple(round(x, 3) for x in v) for k, v in FEET.items()}, "tail", TAIL)
bpy.ops.object.mode_set(mode='OBJECT')
for k in ("neck", "jaw", "wing_L", "wing_R"): print(f"QUACKS joint {k}: {tuple(round(c, 3) for c in P(k))}")

# 5) region weights (bone-heat auto-weighting fails on self-intersecting generated meshes):
#    head above the neck, wings at the sides around shoulder height, lower beak -> jaw, rest -> body,
#    with smooth blends so bends don't tear.
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_NAME')          # creates empty groups per bone
vg = {g.name: g for g in body.vertex_groups}
def smooth(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t)
neck_z, jaw, jaw_tip = P("neck").z, P("jaw"), P("jaw_tip")
sh_x, sh_z, tip_z = abs(P("wing_L").x), P("wing_L").z, P("wing_L_tip").z
counts = {k: 0 for k in vg}
for v in body.data.vertices:
    x, y, z = v.co
    w = {}
    head = smooth(neck_z - 0.03 * H, neck_z + 0.03 * H, z)
    if masks and "jaw_hinge" in FACE:
        # lower beak = front of the face, below a straight mouth line (hinge -> tip), soft 6 mm blend:
        # the colour mask finds the line; cutting along it gives a clean opening edge instead of a jagged one
        hy, hz = FACE["jaw_hinge"][1], FACE["jaw_hinge"][2] + 0.004 * H
        ty, tz = FACE["jaw_tip"][1], FACE["jaw_tip"][2] + 0.012 * H
        tt = min(1.0, max(0.0, (y - hy) / (ty - hy))) if ty != hy else 0.0
        line = hz + (tz - hz) * tt
        in_beak = y < hy + 0.01 * D and 0.6 * H < z < 0.82 * H and abs(x) < 0.2 * W
        beak = (1.0 - smooth(line - 0.005 * H, line + 0.005 * H, z)) if in_beak else 0.0
    elif masks: beak = 1.0 if masks["jaw"][v.index] else 0.0
    else: beak = 1.0 if (y < jaw.y and jaw_tip.z - 0.06 * H < z < jaw.z) else 0.0
    if masks and masks["tie"][v.index]:
        vg["tie"].add([v.index], 1.0, 'REPLACE'); counts["tie"] = counts.get("tie", 0) + 1; continue
    # Option A rigid parts: feet (lowest slab, near each foot) and the back tuft move as solid pieces
    fname = ("foot_L" if np.sign(x) == _lsign else "foot_R")
    if fname in FEET and z < 0.11 * H and abs(x - FEET[fname][0]) < 0.22 * W:
        fw = 1.0 - smooth(0.065 * H, 0.105 * H, z)
        if fw > 0:
            vg[fname].add([v.index], fw, 'REPLACE'); counts[fname] += 1
            if fw < 0.999: vg["body"].add([v.index], 1.0 - fw, 'REPLACE'); counts["body"] += 1
            continue
    if TAIL is not None and y > _ymin + 0.80 * D and 0.15 * H < z < 0.65 * H and abs(x) < 0.32 * W:
        tw = smooth(_ymin + 0.80 * D, _ymin + 0.88 * D, y)
        if tw > 0:
            vg["tail"].add([v.index], tw, 'REPLACE'); counts["tail"] += 1
            if tw < 0.999: vg["body"].add([v.index], 1.0 - tw, 'REPLACE'); counts["body"] += 1
            continue
    side = smooth(sh_x * 0.85, sh_x * 1.0, abs(x)) * smooth(tip_z - 0.12 * H, tip_z - 0.02 * H, z) * (1 - smooth(sh_z, sh_z + 0.06 * H, z))
    if beak >= 0.999: w["jaw"] = 1.0
    elif beak > 0:
        w["jaw"] = beak; w["head"] = 1.0 - beak
    else:
        w["head"] = head
        if side > 0: w["wing_L" if x > 0 else "wing_R"] = side * (1 - head)
        w["body"] = max(0.0, 1.0 - sum(w.values()))
    for name, val in w.items():
        if val > 1e-4: vg[name].add([v.index], val, 'REPLACE'); counts[name] += 1
print("QUACKS region weights (vertices per bone):", counts)
# 6) previews: rest pose and a test pose (beak open, wings raised, head tilted)
def render(path, pose):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 420, 560
    if not sc.world:
        sc.world = bpy.data.worlds.new("w"); sc.world.use_nodes = True
        sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.8, 0.8, 1)
    if "sun" not in bpy.data.objects:
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
        sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = H * 1.2
    cam = sc.camera
    for pb in arm.pose.bones: pb.rotation_mode = 'XYZ'; pb.rotation_euler = (0, 0, 0)
    for name, rx, ry, rz in pose: arm.pose.bones[name].rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
    for tag, ang in (("front", 0), ("side", 90)):
        a = math.radians(ang); d = H * 4
        cam.location = Vector((d * math.sin(a), -d * math.cos(a), H / 2)); cam.rotation_euler = (math.radians(90), 0, a)
        sc.render.filepath = f"{path}_{tag}.png"; bpy.ops.render.render(write_still=True)
if opt["--preview"]:
    render(opt["--preview"] + "_rest", [])
    render(opt["--preview"] + "_pose", [("jaw", 25, 0, 0), ("wing_L", 0, -50, 0), ("wing_R", 0, 50, 0), ("head", 0, 15, 0)] + [(b, 30, 0, 0) for b in ("foot_L", "tail") if b in arm.pose.bones] + ([("tie", 0, 30, 0)] if "tie" in arm.pose.bones else []))
    for pb in arm.pose.bones: pb.rotation_euler = (0, 0, 0)

# 6b) write the base-colour texture next to the FBX (<name>_basecolor.png): the Unity loader builds its
#     URP material from it explicitly instead of relying on FBX material import.
import os
tex_out = os.path.splitext(dst)[0] + "_basecolor.jpg"
base = None
for mat in body.data.materials:
    if mat and mat.use_nodes:
        for node in mat.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED' and node.inputs['Base Color'].is_linked:
                src_node = node.inputs['Base Color'].links[0].from_node
                if src_node.type == 'TEX_IMAGE' and src_node.image: base = src_node.image
if base is None:
    imgs = [i for i in bpy.data.images if i.size[0] > 0 and ("color" in i.name.lower() or "base" in i.name.lower())]
    base = imgs[0] if imgs else None
if base is not None:
    base.filepath_raw = tex_out; base.file_format = 'JPEG'; base.save()   # JPEG keeps the repo small
    print(f"QUACKS base colour texture -> {tex_out} ({base.size[0]}x{base.size[1]})")
else:
    print("QUACKS WARNING: no base colour texture found")

# 7) export FBX for Unity (textures embedded; -Y forward in Blender == +Z forward in Unity)
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); arm.select_set(True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, path_mode='STRIP', embed_textures=False,   # the loader uses <name>_basecolor
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS', bake_anim=False)
if FACE:
    with open(os.path.splitext(dst)[0] + "_face.json", "w") as f: json.dump(FACE, f, indent=1)
    print("QUACKS face metrics ->", os.path.splitext(dst)[0] + "_face.json")
print(f"QUACKS_EXPORT_OK {dst}")
