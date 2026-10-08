#!/usr/bin/env python3
"""
3D-модель машины для Unity по чертежу-референсу из папки Ref/ (4 вида на листе).

Как работает:
  1. ref_views.py находит на листе виды (спереди, сбоку, сзади, сверху), силуэты и колёса.
  2. Силуэты масштабируются по реальным размерам из RacingSim/Assets/Resources/Cars/<id>.json:
     вид сбоку — по базе (расстояние между центрами колёс), спереди/сзади — по ширине.
  3. Кузов = пересечение трёх «выдавленных» силуэтов (visual hull) на воксельной сетке,
     минус колёсные арки; сглаживание и marching cubes → полигональная сетка.
  4. В Blender (модуль bpy): упрощение сетки, материалы по проекциям чертежа
     (стёкла, фары, фонари, решётки, карбон снизу), отдельные объекты — антикрыло,
     сплиттер, диффузор, зеркала, колесо; UV-развёртка; экспорт FBX для Unity + .blend + превью.

Система координат Blender: нос по −Y, верх +Z, X — вправо; начало — на земле посередине
между осями. Экспорт FBX (−Z Forward, Y Up, Apply Transform) → в Unity нос по +Z, как ждёт игра.

Результат:
    RacingSim/Assets/Resources/CarModels/<id>.fbx  — кузов для игры (без колёс)
    art/models/<id>/<id>.blend                     — исходник для доработки в Blender
    art/models/<id>/<id>_Wheel.fbx                 — колесо (диск + шина) отдельно
    art/models/<id>/preview_*.jpg                  — превью

Запуск (нужны: pip install bpy scikit-image scipy pillow numpy):
    python build_car_model.py               # все машины
    python build_car_model.py BMWM4GT3      # одна
"""
import json
import math
import os
import sys

import numpy as np
from scipy import ndimage as ndi
from scipy.ndimage import median_filter
from skimage.measure import marching_cubes

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_views as rv  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
CARS_DIR = os.path.join(ROOT, "RacingSim", "Assets", "Resources", "Cars")
OUT_ART = os.path.join(ROOT, "art", "models")
OUT_UNITY = os.path.join(ROOT, "RacingSim", "Assets", "Resources", "CarModels")

CARS = {
    "AstonMartinVantageGT3": "Aston Martin GT3.png",
    "Ferrari296GT3": "Ferrari 296 GT3.png",
    "BMWM4GT3": "BMW M4.png",
}
# длина заднего стекла (м) за боковыми окнами: фастбэк Aston — длинное, купе BMW — короче,
# у Ferrari за кабиной крышка мотора, а не стекло
REAR_GLASS = {"AstonMartinVantageGT3": 0.55, "Ferrari296GT3": 0.12, "BMWM4GT3": 0.32}

VOXEL = 0.017  # м
RIDE = 0.095   # нижняя кромка кузова, м


# ---------------------------------------------------------------------------
# 1. Профили из чертежа
# ---------------------------------------------------------------------------
class Profiles:
    pass


def monotone_roof(top_y, x0, x1):
    """Верх кузова (в пикселях, меньше = выше): от пика крыши (ищется между колёсами x0..x1,
    чтобы не принять за крышу высокое антикрыло) к носу и к корме только опускается."""
    t = top_y.copy()
    valid = t >= 0
    idx = np.nonzero(valid)[0]
    cab = idx[(idx >= x0) & (idx <= x1)]
    peak = cab[np.argmin(t[cab])]
    for x in range(peak + 1, idx[-1] + 1):  # к корме: y не уменьшается (срезает крыло)
        if t[x] >= 0 and t[x - 1] >= 0:
            t[x] = max(t[x], t[x - 1])
    for x in range(peak - 1, idx[0] - 1, -1):  # к носу
        if t[x] >= 0 and t[x + 1] >= 0:
            t[x] = max(t[x], t[x + 1])
    return t


def envelope_from_end_view(gray_view):
    """Полуширина вида спереди/сзади по строкам; выше самой широкой строки — только сужение (без крыла и зеркал)."""
    m = rv.silhouette(gray_view, 3, 0)
    m = ndi.binary_opening(m, structure=np.ones((9, 9)))  # без антенны и тонких стоек
    H, W = m.shape
    cx = W // 2
    hw = median_filter(rv.row_halfwidth(m, cx, 0, H), 7)
    ground = rv.ground_row(gray_view)
    rows = np.nonzero(hw > 0)[0]
    lower = rows[rows > rows[0] + 0.45 * (ground - rows[0])]
    wide_row = lower[np.argmax(hw[lower])]
    env = hw.copy()
    for y in range(wide_row - 1, -1, -1):
        env[y] = min(env[y], env[y + 1]) if env[y] > 0 else 0
    # верх силуэта (крыша) — первая строка с ненулевой шириной после очистки
    return env, ground, float(hw[wide_row])


def extract(car_id, ref_path, spec):
    d = spec["dimensions"]
    g = rv.load_gray(ref_path)
    views = rv.find_views(g)
    P = Profiles()

    # --- вид сбоку ---
    side = rv.crop(g, views["side"])
    W = side.shape[1]
    wheels = rv.wheels_side(side, int(W / 4.8 * 0.30), int(W / 4.8 * 0.40))
    (xf, yf, rf), (xr, yr, rr) = wheels
    scale = (xr - xf) / d["wheelbase"]  # px/м
    ground = rv.ground_row(side)
    m = rv.silhouette(side, 3, 0)
    # тонкие детали (антенна, стойки крыла) не должны задавать высоту кузова
    m_body = ndi.binary_opening(m, structure=np.ones((9, 9)))
    top = rv.column_top(m_body, int(min(yf, yr))).astype(float)
    top = median_filter(top, 21)
    top = monotone_roof(top, int(xf), int(xr))
    xmid = (xf + xr) / 2
    cols = np.nonzero(top >= 0)[0]
    # продольная координата z (вперёд +), высота y
    P.side_z = -(cols - xmid) / scale
    P.side_top = (ground - top[cols]) / scale
    P.scale_side = scale
    P.wheel_r_img = ((rf + rr) / 2) / scale
    P.z_nose, P.z_tail = P.side_z.max(), P.side_z.min()

    # --- вид сверху: полуширина по z ---
    topv = rv.crop(g, views["top"])
    tm = rv.silhouette(topv, 3, 0)
    cy = topv.shape[0] // 2
    hw = median_filter(rv.col_halfwidth(tm, cy), 31)
    tcols = np.nonzero(hw > 0)[0]
    n_px, t_px = tcols.min(), tcols.max()
    # нос/корма вида сверху совмещаются с носом/кормой вида сбоку
    z_of = lambda px: P.z_nose + (px - n_px) * (P.z_tail - P.z_nose) / (t_px - n_px)  # noqa: E731
    tscale = (t_px - n_px) / (P.z_nose - P.z_tail)
    P.plan_z = np.array([z_of(px) for px in tcols])
    P.plan_hw = np.minimum(hw[tcols] / tscale, d["width"] / 2)

    # --- виды спереди и сзади: полуширина по высоте ---
    P.end = {}
    for name in ("front", "back"):
        v = rv.crop(g, views[name])
        env, gr, wide = envelope_from_end_view(v)
        s = wide / (d["width"] / 2)
        rows = np.nonzero(env > 0)[0]
        ys = (gr - rows) / s
        setattr(P, name + "_y", ys[::-1])
        setattr(P, name + "_hw", (env[rows] / s)[::-1])
        P.end[name] = {"cx": v.shape[1] / 2, "ground": gr, "scale": s, "shape": v.shape}

    # стёкла на виде сбоку и антикрыло (то, что выше профиля кузова в задней части)
    roof_row = int(np.nonzero(m[:, int(xf):int(xr)].any(axis=1))[0].min())
    P.side_glass = rv.side_glass_mask(side, ground, roof_row, xf, xr + rr)
    P.side_mask, P.side_shape = m, side.shape
    above = np.zeros_like(m)
    full_top = np.full(W, side.shape[0])
    full_top[cols] = top[cols]
    for x in range(int(xmid), W):
        above[: max(int(full_top[x]) - 3, 0), x] = m[: max(int(full_top[x]) - 3, 0), x]
    lab, n = ndi.label(above)
    if n:
        sizes = ndi.sum(above, lab, range(1, n + 1))
        P.wing_px = lab == (1 + int(np.argmax(sizes)))
    else:
        P.wing_px = None

    # --- подгонка под реальные размеры: база сохраняется (по ней масштабирован вид сбоку),
    #     свесы растягиваются до паспортных, высота — до паспортной ---
    hb = d["wheelbase"] / 2
    fo_img, ro_img = P.z_nose - hb, -P.z_tail - hb
    fo, ro = d["frontOverhang"], d["length"] - d["wheelbase"] - d["frontOverhang"]

    def warp(z):
        z = np.asarray(z, float)
        return np.where(z > hb, hb + (z - hb) * fo / fo_img, np.where(z < -hb, -hb + (z + hb) * ro / ro_img, z))

    P.fit = {"length_img": float(P.z_nose - P.z_tail), "height_img": float(P.side_top.max()),
             "overhang_img": (float(fo_img), float(ro_img))}
    P.side_z = warp(P.side_z)
    P.plan_z = warp(P.plan_z)
    P.side_top = P.side_top * d["height"] / P.side_top.max()
    P.front_hmax_img, P.back_hmax_img = float(P.front_y.max()), float(P.back_y.max())
    P.front_y = P.front_y * d["height"] / P.front_y.max()
    P.back_y = P.back_y * d["height"] / P.back_y.max()
    P.z_nose, P.z_tail = P.side_z.max(), P.side_z.min()
    P.warp = warp

    def unwarp(z):
        z = np.asarray(z, float)
        return np.where(z > hb, hb + (z - hb) * fo_img / fo, np.where(z < -hb, -hb + (z + hb) * ro_img / ro, z))

    hfac_side = P.fit["height_img"] / d["height"]
    P.to_side_px = lambda z, y: (xmid - unwarp(z) * scale, ground - np.asarray(y) * hfac_side * scale)  # noqa: E731
    P.from_side_px = lambda px, py: (warp(-(np.asarray(px) - xmid) / scale), (ground - np.asarray(py)) / scale / hfac_side)  # noqa: E731
    for name in ("front", "back"):
        e = P.end[name]
        e["hfac"] = getattr(P, name + "_hmax_img") / d["height"]
    P.views, P.gray = views, g
    P.wheels_px, P.ground_px, P.xmid_px = wheels, ground, xmid
    return P


# ---------------------------------------------------------------------------
# 2. Воксельный visual hull → сетка
# ---------------------------------------------------------------------------
def build_hull(P, spec):
    d = spec["dimensions"]
    tf, tr = d["trackFront"], d["trackRear"]
    wf, wr = spec["tyreFront"]["width"], spec["tyreRear"]["width"]
    rf, rr = spec["tyreFront"]["radius"], spec["tyreRear"]["radius"]
    zf, zr = d["wheelbase"] / 2, -d["wheelbase"] / 2

    zs = np.arange(P.z_tail - 0.03, P.z_nose + 0.03, VOXEL)
    ys = np.arange(0.0, max(P.side_top.max(), d["height"]) + 0.06, VOXEL)
    xs = np.arange(-d["width"] / 2 - 0.04, d["width"] / 2 + 0.04, VOXEL)

    top = np.interp(zs, P.side_z[::-1], P.side_top[::-1], left=0, right=0)
    plan = np.interp(zs, P.plan_z[::-1], P.plan_hw[::-1], left=0, right=0)
    env_f = np.interp(ys, P.front_y, P.front_hw, left=P.front_hw[0], right=0)
    env_b = np.interp(ys, P.back_y, P.back_hw, left=P.back_hw[0], right=0)
    # спереди — сечение по виду спереди, сзади — по виду сзади, между осями — плавный переход
    t = np.clip((zf - zs) / (zf - zr), 0, 1)
    t = t * t * (3 - 2 * t)

    Z, Y, X = np.meshgrid(zs, ys, xs, indexing="ij")
    T = t[:, None, None]
    env = (1 - T) * env_f[None, :, None] + T * env_b[None, :, None]
    # поперечный изгиб верха: к краям верх опускается (капот, крыша, крышка багажника)
    rel = np.abs(X) / np.maximum(np.minimum(plan[:, None, None], env), 1e-3)
    crown = 0.07 * np.clip(rel, 0, 1) ** 3
    occ = (Y <= top[:, None, None] - crown) & (Y >= RIDE) & (np.abs(X) <= plan[:, None, None]) & (np.abs(X) <= env)

    # колёсные арки
    for zc, r, trk, w in ((zf, rf, tf, wf), (zr, rr, tr, wr)):
        inner = trk / 2 - w / 2 - 0.05
        arch = ((Z - zc) ** 2 + (Y - r) ** 2 < (r + 0.045) ** 2) & (np.abs(X) > inner)
        occ &= ~arch

    field = ndi.gaussian_filter(occ.astype(np.float32), 1.8)
    field = np.pad(field, 2)
    verts, faces, _, _ = marching_cubes(field, 0.5, spacing=(VOXEL, VOXEL, VOXEL))
    verts -= 2 * VOXEL
    # (z, y, x) индексы → метры
    vz = verts[:, 0] + zs[0]
    vy = verts[:, 1] + ys[0]
    vx = verts[:, 2] + xs[0]
    v = taubin(np.column_stack([vx, vz, vy]), faces, 50)
    return v, faces  # (x вправо, z вперёд, y вверх)


def taubin(v, f, iters=30, lam=0.5, mu=-0.53):
    """Сглаживание Таубина: убирает «ступеньки» вокселей, почти не уменьшая объём."""
    from scipy.sparse import coo_matrix, diags
    n = len(v)
    i = np.concatenate([f[:, 0], f[:, 1], f[:, 2], f[:, 1], f[:, 2], f[:, 0]])
    j = np.concatenate([f[:, 1], f[:, 2], f[:, 0], f[:, 0], f[:, 1], f[:, 2]])
    A = coo_matrix((np.ones(len(i)), (i, j)), shape=(n, n)).tocsr()
    A.data[:] = 1.0
    deg = np.asarray(A.sum(axis=1)).ravel()
    Dinv = diags(1.0 / np.maximum(deg, 1))
    Wm = Dinv @ A
    for _ in range(iters):
        v = v + lam * (Wm @ v - v)
        v = v + mu * (Wm @ v - v)
    return v


# ---------------------------------------------------------------------------
# 3. Blender: сборка, материалы, экспорт
# ---------------------------------------------------------------------------
def poly_mask(shape, polys):
    from skimage.draw import polygon as draw_polygon
    m = np.zeros(shape[:2], bool)
    for p in polys:
        p = np.array(p)
        rr, cc = draw_polygon(p[:, 1], p[:, 0], shape[:2])
        m[rr, cc] = True
    return m


def sample(mask, px, py):
    H, W = mask.shape
    xi = np.clip(np.round(px).astype(int), 0, W - 1)
    yi = np.clip(np.round(py).astype(int), 0, H - 1)
    inside = (px >= 0) & (px < W) & (py >= 0) & (py < H)
    return mask[yi, xi] & inside


def make_material(bpy, name, color, rough=0.4, metal=0.0, emit=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    mat.diffuse_color = (*color, 1.0)  # цвет для превью и FBX
    return mat


def mesh_object(bpy, name, verts, faces, mats=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    me.validate()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for m in mats or []:
        me.materials.append(m)
    return ob


def to_blender(x, z, y):
    """Модель (x вправо, z вперёд, y вверх) → Blender (X вправо, Y назад: нос по −Y, Z вверх)."""
    return np.column_stack([x, -np.asarray(z), y])


def extrude_profile(bpy, name, prof_zy, x0, x1, mat):
    """Профиль в плоскости (z, y) выдавливается по X от x0 до x1 (крыло, пластины)."""
    import bmesh
    bm = bmesh.new()
    ring0 = [bm.verts.new((x0, -z, y)) for z, y in prof_zy]
    ring1 = [bm.verts.new((x1, -z, y)) for z, y in prof_zy]
    n = len(prof_zy)
    bm.faces.new(ring0[::-1])
    bm.faces.new(ring1)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((ring0[i], ring0[j], ring1[j], ring1[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(mat)
    return ob


def box(bpy, name, center, size, mat, bevel=0.0):
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = v.co.x * size[0] + center[0]
        v.co.y = v.co.y * size[1] + center[1]
        v.co.z = v.co.z * size[2] + center[2]
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(mat)
    return ob


def make_wheel(bpy, name, radius, width, mats):
    """Колесо (ось X, центр в начале координат): шина + диск с 10 спицами + гайка."""
    import bmesh
    from mathutils import Matrix
    bm = bmesh.new()
    # профиль шины в плоскости (ось X, радиус Z) → вращение вокруг X
    r_in = radius * 0.70
    hw = width / 2
    prof = [(-hw * 0.9, r_in), (-hw, r_in + 0.02), (-hw, radius - 0.04), (-hw * 0.85, radius - 0.006),
            (-hw * 0.5, radius), (hw * 0.5, radius), (hw * 0.85, radius - 0.006), (hw, radius - 0.04),
            (hw, r_in + 0.02), (hw * 0.9, r_in)]
    vs = [bm.verts.new((x, 0.0, z)) for x, z in prof]
    edges = [bm.edges.new((vs[i], vs[i + 1])) for i in range(len(vs) - 1)]
    bmesh.ops.spin(bm, geom=vs + edges, angle=math.radians(360), steps=48, axis=(1, 0, 0), cent=(0, 0, 0), use_duplicate=False)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    for f in bm.faces:
        f.material_index = 0
        f.smooth = True
    # диск
    rim = bmesh.ops.create_cone(bm, cap_ends=True, segments=40, radius1=r_in, radius2=r_in, depth=width * 0.82,
                                matrix=Matrix.Rotation(math.radians(90), 4, "Y"))
    for v in rim["verts"]:
        pass
    rim_faces = {f for v in rim["verts"] for f in v.link_faces}
    for f in rim_faces:
        f.material_index = 1
    # спицы и гайка (наружная сторона +X)
    for k in range(10):
        a = 2 * math.pi * k / 10
        sp = bmesh.ops.create_cube(bm, size=1.0)
        for v in sp["verts"]:
            v.co.x = v.co.x * 0.03 + width * 0.42
            v.co.y *= 0.045
            v.co.z = v.co.z * r_in * 0.86 + r_in * 0.45
            y, z = v.co.y, v.co.z
            v.co.y, v.co.z = y * math.cos(a) - z * math.sin(a), y * math.sin(a) + z * math.cos(a)
        for f in {f for v in sp["verts"] for f in v.link_faces}:
            f.material_index = 2
    nut = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.05, radius2=0.04, depth=0.05,
                                matrix=Matrix.Translation((width * 0.46, 0, 0)) @ Matrix.Rotation(math.radians(90), 4, "Y"))
    for f in {f for v in nut["verts"] for f in v.link_faces}:
        f.material_index = 3
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for m in mats:
        me.materials.append(m)
    return ob


def assign_body_materials(bpy, ob, P, spec, zones, belt_y, glass_z):
    """Материал каждой грани кузова — по проекции её центра на виды чертежа."""
    me = ob.data
    n = len(me.polygons)
    cen = np.zeros(n * 3)
    nor = np.zeros(n * 3)
    me.polygons.foreach_get("center", cen)
    me.polygons.foreach_get("normal", nor)
    cen = cen.reshape(-1, 3)
    nor = nor.reshape(-1, 3)
    x, z, y = cen[:, 0], -cen[:, 1], cen[:, 2]
    nx, nfwd, nup = nor[:, 0], -nor[:, 1], nor[:, 2]
    top = np.interp(z, P.side_z[::-1], P.side_top[::-1])

    idx = np.zeros(n, int)  # 0 краска
    # стёкла
    spx, spy = P.to_side_px(z, y)
    side_glass = sample(P.side_glass, spx, spy)
    lateral = np.abs(nx) > 0.55
    env = np.interp(y, P.front_y, P.front_hw)
    on_top = y > top - 0.06
    inner = np.abs(x) < env * 0.86
    # крыша — где профиль в пределах 3.5 см от максимума; спереди — лобовое, сзади — заднее стекло
    zp = P.side_z[np.argmax(P.side_top)]
    peak = P.side_top.max()
    roof_zone = np.interp(z, P.side_z[::-1], P.side_top[::-1]) > peak - 0.035
    upper = on_top & inner & (y > belt_y + 0.03) & ~roof_zone
    windshield = upper & (z > zp) & (z < glass_z[1] + 0.9)
    rear_win = upper & (z < zp) & (z > glass_z[0] - P.rear_glass)
    glass = (lateral & side_glass & (y > belt_y)) | windshield | rear_win
    idx[glass] = 1
    # фары / решётки / фонари
    for view, sign in (("front", 1), ("back", -1)):
        e = P.end[view]
        facing = nfwd * sign > 0.35
        px = e["cx"] + x * e["scale"] * (1 if view == "back" else -1)
        py = e["ground"] - y * e["hfac"] * e["scale"]
        for kind, mi in (("grille", 3), ("headlight", 4), ("taillight", 5)):
            polys = zones[view].get(kind)
            if not polys:
                continue
            hit = sample(poly_mask(e["shape"], polys), px, py) & facing
            idx[hit] = mi
    idx[(y < 0.17) & (idx == 0)] = 2  # низ — карбон
    me.polygons.foreach_set("material_index", idx)
    me.update()
    return {k: int((idx == i).sum()) for i, k in enumerate(["paint", "glass", "carbon", "grille", "headlight", "taillight"])}


def build_in_blender(car_id, P, spec, verts, faces, out_dir, unity_dir, target_tris=180000):
    import bpy
    import bmesh
    from zones import ZONES

    bpy.ops.wm.read_factory_settings(use_empty=True)
    d = spec["dimensions"]
    col = spec.get("bodyColor", [0.5, 0.5, 0.5])
    acc = spec.get("accentColor", [0.9, 0.9, 0.9])
    M = {
        "paint": make_material(bpy, f"Paint_{car_id}", col, 0.25, 0.3),
        "glass": make_material(bpy, "Glass", (0.03, 0.04, 0.05), 0.05, 0.0),
        "carbon": make_material(bpy, "Carbon", (0.035, 0.035, 0.04), 0.35, 0.2),
        "grille": make_material(bpy, "Grille", (0.015, 0.015, 0.015), 0.6, 0.0),
        "headlight": make_material(bpy, "Headlight", (0.9, 0.9, 0.85), 0.1, 0.0, emit=(1.0, 0.97, 0.85)),
        "taillight": make_material(bpy, "Taillight", (0.6, 0.02, 0.02), 0.15, 0.0, emit=(1.0, 0.05, 0.03)),
        "accent": make_material(bpy, f"Accent_{car_id}", acc, 0.3, 0.2),
        "tyre": make_material(bpy, "Tyre", (0.025, 0.025, 0.025), 0.8, 0.0),
        "rim": make_material(bpy, "Rim", (0.55, 0.56, 0.58), 0.25, 0.9),
        "spoke": make_material(bpy, "RimSpoke", (0.12, 0.12, 0.13), 0.3, 0.8),
        "nut": make_material(bpy, "WheelNut", (0.85, 0.65, 0.05), 0.3, 0.9),
    }

    # --- кузов ---
    bv = to_blender(verts[:, 0], verts[:, 1], verts[:, 2])
    body = mesh_object(bpy, "Body", bv, faces,
                       [M["paint"], M["glass"], M["carbon"], M["grille"], M["headlight"], M["taillight"]])
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    dec = body.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, target_tris / max(len(body.data.polygons), 1))
    dg = bpy.context.evaluated_depsgraph_get()
    new_me = bpy.data.meshes.new_from_object(body.evaluated_get(dg))
    body.modifiers.clear()
    old = body.data
    body.data = new_me
    bpy.data.meshes.remove(old)
    for m in [M["paint"], M["glass"], M["carbon"], M["grille"], M["headlight"], M["taillight"]]:
        if m.name not in [x.name for x in body.data.materials]:
            body.data.materials.append(m)

    # пояс стёкол и продольный диапазон кабины из маски стёкол вида сбоку
    gy, gx = np.nonzero(P.side_glass)
    gz, gyy = P.from_side_px(gx, gy)
    belt_y = float(np.percentile(gyy, 3)) - 0.01
    glass_z = (float(gz.min()) - 0.02, float(gz.max()) + 0.02)
    stats = assign_body_materials(bpy, body, P, spec, ZONES[car_id], belt_y, glass_z)
    body.data.shade_smooth()
    try:
        body.data.set_sharp_from_angle(angle=math.radians(50))
    except Exception:  # noqa: BLE001
        pass

    parts = [body]
    hb = d["wheelbase"] / 2
    W = d["width"]

    # --- антикрыло ---
    if P.wing_px is not None and P.wing_px.sum() > 50:
        wy, wx = np.nonzero(P.wing_px)
        wz, wyy = P.from_side_px(wx, wy)
        ytop = wyy.max()
        prof_sel = wyy > ytop - 0.11
        zmin, zmax = float(wz[prof_sel].min()), float(wz[prof_sel].max())
        chord = zmax - zmin
        # профиль: по столбцам — верх и низ выделенных пикселей
        zs = np.linspace(zmin, zmax, 14)
        upper, lower = [], []
        for zc in zs:
            sel = prof_sel & (np.abs(wz - zc) < chord / 20 + 0.01)
            if sel.sum() == 0:
                continue
            upper.append((zc, float(wyy[sel].max())))
            lower.append((zc, max(float(wyy[sel].min()), float(wyy[sel].max()) - 0.06)))
        prof = upper + lower[::-1]
        span = 0.9 * W
        wing = extrude_profile(bpy, "Wing", prof, -span / 2, span / 2, M["carbon"])
        ylo = min(p[1] for p in lower)
        plate = [(zmin - 0.04, ylo - 0.12), (zmax + 0.04, ylo - 0.08), (zmax + 0.04, ytop + 0.05), (zmin, ytop + 0.04)]
        ep_l = extrude_profile(bpy, "WingEndplateL", plate, -span / 2 - 0.008, -span / 2, M["carbon"])
        ep_r = extrude_profile(bpy, "WingEndplateR", plate, span / 2, span / 2 + 0.008, M["carbon"])
        zm = (zmin + zmax) / 2
        deck = float(np.interp(zm, P.side_z[::-1], P.side_top[::-1]))
        mounts = [box(bpy, f"WingMount{s}", (s * 0.28, -zm, (deck + ylo) / 2), (0.016, 0.16, ylo - deck + 0.04), M["carbon"])
                  for s in (-1, 1)]
        parts += [wing, ep_l, ep_r] + mounts

    # --- сплиттер и диффузор ---
    zf_nose = P.z_nose
    zs = np.linspace(hb + 0.05, zf_nose + 0.04, 16)
    pw = np.interp(zs, P.plan_z[::-1], P.plan_hw[::-1]) * 0.97
    outline = [(-w, z) for z, w in zip(zs, pw)] + [(w, z) for z, w in zip(zs[::-1], pw[::-1])]
    sp = bmesh.new()
    lo = [sp.verts.new((x, -z, 0.055)) for x, z in outline]
    hi = [sp.verts.new((x, -z, 0.075)) for x, z in outline]
    sp.faces.new(lo)
    sp.faces.new(hi[::-1])
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        sp.faces.new((lo[i], hi[i], hi[j], lo[j]))
    bmesh.ops.recalc_face_normals(sp, faces=sp.faces)
    sme = bpy.data.meshes.new("Splitter")
    sp.to_mesh(sme)
    sp.free()
    splitter = bpy.data.objects.new("Splitter", sme)
    bpy.context.scene.collection.objects.link(splitter)
    sme.materials.append(M["carbon"])
    parts.append(splitter)
    ztail = P.z_tail
    dlen = (-hb + 0.1) - ztail
    parts.append(box(bpy, "Diffuser", (0, -(ztail + dlen / 2), 0.1), (W * 0.72, dlen, 0.014), M["carbon"]))
    for k in range(-2, 3):
        parts.append(box(bpy, f"DiffuserFin{k + 2}", (k * W * 0.14, -(ztail + 0.25), 0.16), (0.008, 0.5, 0.11), M["carbon"]))

    # --- зеркала ---
    zmir = glass_z[1] - 0.12
    ymir = belt_y + 0.07
    env = np.interp(ymir, P.front_y, P.front_hw)
    for s in (-1, 1):
        parts.append(box(bpy, f"Mirror{'L' if s < 0 else 'R'}", (s * (env + 0.1), -zmir, ymir), (0.17, 0.09, 0.09), M["paint"], bevel=0.02))
        parts.append(box(bpy, f"MirrorArm{'L' if s < 0 else 'R'}", (s * (env + 0.03), -zmir, ymir - 0.02), (0.08, 0.03, 0.02), M["carbon"]))

    # --- общий корень ---
    root = bpy.data.objects.new(car_id, None)
    bpy.context.scene.collection.objects.link(root)
    for p in parts:
        p.parent = root

    # --- UV для будущих ливрей ---
    for p in parts:
        bpy.context.view_layer.objects.active = p
        for o in bpy.context.selected_objects:
            o.select_set(False)
        p.select_set(True)
        try:
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
            bpy.ops.object.mode_set(mode="OBJECT")
        except Exception as e:  # noqa: BLE001
            print("UV:", p.name, e)
            if bpy.context.object and bpy.context.object.mode != "OBJECT":
                bpy.ops.object.mode_set(mode="OBJECT")

    # --- колесо (отдельный объект/файл; в игре колёса рисует физика) ---
    wheel = make_wheel(bpy, f"{car_id}_Wheel", spec["tyreRear"]["radius"], spec["tyreRear"]["width"],
                       [M["tyre"], M["rim"], M["spoke"], M["nut"]])
    wheel.location = (d["trackRear"] / 2, hb, spec["tyreRear"]["radius"])  # для превью — на месте заднего правого

    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(unity_dir, exist_ok=True)
    tris = sum(len(p.data.polygons) for p in parts)

    def export(objs, path):
        for o in bpy.context.scene.objects:
            o.select_set(False)
        for o in objs:
            o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
                                 apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE",
                                 add_leaf_bones=False, path_mode="AUTO")

    # кузов — сразу в Resources игры (CarBodyBuilder подхватит его вместо процедурного)
    export([root] + parts, os.path.join(unity_dir, f"{car_id}.fbx"))
    wl = wheel.location.copy()
    wheel.location = (0, 0, 0)
    export([wheel], os.path.join(out_dir, f"{car_id}_Wheel.fbx"))
    wheel.location = wl

    # превью: остальные 3 колеса — копии
    for s, zc, nm in ((-1, hb, "RL"), (1, -hb, "FR"), (-1, -hb, "FL")):
        c = wheel.copy()
        c.location = (s * (d["trackFront"] if zc < 0 else d["trackRear"]) / 2, zc, spec["tyreRear"]["radius"])
        if s < 0:
            c.rotation_euler = (0, 0, math.pi)
        bpy.context.scene.collection.objects.link(c)
    bpy.context.preferences.filepaths.save_version = 0  # без .blend1
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, f"{car_id}.blend"), compress=True)
    if os.environ.get("NO_PREVIEW") != "1":
        render_previews(bpy, out_dir, d)
    return {"tris": tris, "materials": stats, "fit": P.fit, "belt_y": belt_y}


def render_previews(bpy, out_dir, d):
    from mathutils import Euler
    sc = bpy.context.scene
    # Cycles на CPU — работает без видеокарты (Workbench/EEVEE в режиме без окна требуют GPU)
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1200, 680
    sc.render.film_transparent = False
    world = bpy.data.worlds.new("Studio")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.82, 0.85, 0.9, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sc.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = Euler((math.radians(40), math.radians(15), math.radians(-30)))
    sc.collection.objects.link(sun)
    # пол
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=12)
    fme = bpy.data.meshes.new("Floor")
    bm.to_mesh(fme)
    bm.free()
    floor = bpy.data.objects.new("Floor", fme)
    sc.collection.objects.link(floor)
    fme.materials.append(make_material(bpy, "FloorMat", (0.55, 0.57, 0.6), 0.8))
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    L = d["length"]
    views = {
        "side": ((-8, 0, 0.6), (math.radians(90), 0, math.radians(-90)), "ORTHO", L * 1.15),
        "front": ((0, -8, 0.6), (math.radians(90), 0, 0), "ORTHO", d["width"] * 1.5),
        "back": ((0, 8, 0.6), (math.radians(90), 0, math.radians(180)), "ORTHO", d["width"] * 1.5),
        "top": ((0, 0, 8), (0, 0, 0), "ORTHO", L * 1.15),
        "persp": ((-5.2, -5.6, 2.6), (math.radians(68), 0, math.radians(-43)), "PERSP", 0),
        "persp_rear": ((5.0, 5.6, 2.4), (math.radians(70), 0, math.radians(138)), "PERSP", 0),
    }
    for name, (loc, rot, kind, scale) in views.items():
        cam.location = loc
        cam.rotation_euler = Euler(rot)
        cam_data.type = kind
        if kind == "ORTHO":
            cam_data.ortho_scale = scale
        else:
            cam_data.lens = 40
        sc.render.image_settings.file_format = "JPEG"
        sc.render.image_settings.quality = 88
        sc.render.filepath = os.path.join(out_dir, f"preview_{name}.jpg")
        bpy.ops.render.render(write_still=True)


def main():
    ids = sys.argv[1:] or list(CARS)
    for cid in ids:
        spec = json.load(open(os.path.join(CARS_DIR, cid + ".json"), encoding="utf-8"))
        P = extract(cid, os.path.join(ROOT, "Ref", CARS[cid]), spec)
        P.rear_glass = REAR_GLASS.get(cid, 0.3)
        verts, faces = build_hull(P, spec)
        res = build_in_blender(cid, P, spec, verts, faces, os.path.join(OUT_ART, cid), OUT_UNITY)
        print(cid, json.dumps(res, ensure_ascii=False))


if __name__ == "__main__":
    main()
