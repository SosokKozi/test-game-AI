"""
Разбор чертежа-референса (4 вида на одном листе) в силуэты, пригодные для построения модели.

Лист: сверху слева — вид спереди, по центру — сбоку (нос слева), справа — сзади; снизу — вид сверху
(нос слева). Виды находятся автоматически как крупные связные области линий.
"""
import numpy as np
from PIL import Image
from scipy import ndimage as ndi
from skimage.transform import hough_circle, hough_circle_peaks
from skimage.feature import canny


def load_gray(path):
    return np.asarray(Image.open(path).convert("L"), float)


def find_views(gray):
    H, W = gray.shape
    ink = gray < 150
    lab, _ = ndi.label(ndi.binary_dilation(ink, iterations=6))
    boxes = []
    for i, sl in enumerate(ndi.find_objects(lab)):
        h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        if w > 0.8 * W or h > 0.8 * H or w * h < 20000:
            continue
        boxes.append((sl[0].start, sl[0].stop, sl[1].start, sl[1].stop))
    boxes.sort(key=lambda b: b[0])
    upper = sorted([b for b in boxes if b[0] < H * 0.45], key=lambda b: b[2])
    lower = [b for b in boxes if b[0] >= H * 0.45]
    assert len(upper) == 3 and len(lower) == 1, boxes
    return {"front": upper[0], "side": upper[1], "back": upper[2], "top": lower[0]}


def crop(a, box, pad=4):
    y0, y1, x0, x1 = box
    return a[max(0, y0 - pad):y1 + pad, max(0, x0 - pad):x1 + pad]


def silhouette(gray_view, close=3, open_=0):
    ink = gray_view < 170
    m = ndi.binary_closing(ink, iterations=close)
    m = ndi.binary_fill_holes(m)
    if open_:
        m = ndi.binary_opening(m, structure=np.ones((open_, open_)))
    # крупнейшая связная область
    lab, n = ndi.label(m)
    if n > 1:
        sizes = ndi.sum(m, lab, range(1, n + 1))
        m = lab == (1 + int(np.argmax(sizes)))
    return m


def ground_row(gray_view):
    """Нижняя длинная горизонтальная линия (земля) или нижняя граница рисунка."""
    ink = gray_view < 170
    rows = ink.sum(axis=1)
    H = len(rows)
    cand = [r for r in range(H - 1, H // 2, -1) if rows[r] > 0.5 * ink.shape[1]]
    if cand:
        return cand[0]
    return max(np.nonzero(rows > 3)[0])


def wheels_side(gray_view, r_min, r_max):
    """Два колеса на виде сбоку: окружности в нижней половине."""
    edges = canny(gray_view, sigma=1.5)
    H = edges.shape[0]
    edges[: H // 3] = False
    radii = np.arange(r_min, r_max + 1)
    acc = hough_circle(edges, radii)
    _, cx, cy, rr = hough_circle_peaks(acc, radii, total_num_peaks=12, min_xdistance=int(r_max * 1.5), min_ydistance=10)
    found = sorted(zip(cx, cy, rr), key=lambda t: t[0])
    # две самые сильные, разнесённые по горизонтали
    best = []
    for c in zip(cx, cy, rr):
        if all(abs(c[0] - b[0]) > r_max * 3 for b in best):
            best.append(c)
        if len(best) == 2:
            break
    return sorted(best, key=lambda t: t[0])


def column_top(mask, y_start):
    """Для каждого столбца: верх непрерывного заполнения, идя вверх от строки y_start (−1 — пусто)."""
    H, W = mask.shape
    top = np.full(W, -1)
    for x in range(W):
        if not mask[y_start, x]:
            continue
        y = y_start
        while y > 0 and mask[y - 1, x]:
            y -= 1
        top[x] = y
    return top


def row_halfwidth(mask, cx, y0, y1):
    """Для каждой строки: полуширина непрерывного заполнения от центрального столбца cx."""
    H, W = mask.shape
    hw = np.zeros(H)
    for y in range(y0, y1):
        if not mask[y, cx]:
            continue
        l = r = cx
        while l > 0 and mask[y, l - 1]:
            l -= 1
        while r < W - 1 and mask[y, r + 1]:
            r += 1
        hw[y] = (r - l) / 2
    return hw


def col_halfwidth(mask, cy):
    """Для каждого столбца (вид сверху): полуширина непрерывного заполнения от осевой строки cy."""
    H, W = mask.shape
    hw = np.zeros(W)
    for x in range(W):
        if not mask[cy, x]:
            continue
        t = b = cy
        while t > 0 and mask[t - 1, x]:
            t -= 1
        while b < H - 1 and mask[b + 1, x]:
            b += 1
        hw[x] = (b - t) / 2
    return hw


def enclosed_regions(gray_view, min_area=150, max_area=60000):
    """Замкнутые светлые области между линиями (окна, фары и т.п.): метки и свойства."""
    ink = ndi.binary_dilation(gray_view < 170, iterations=1)
    lab, n = ndi.label(~ink)
    H, W = ink.shape
    regions = []
    for i, sl in enumerate(ndi.find_objects(lab)):
        reg = lab[sl] == i + 1
        area = int(reg.sum())
        if area < min_area or area > max_area:
            continue
        if sl[0].start == 0 or sl[1].start == 0 or sl[0].stop == H or sl[1].stop == W:
            continue  # фон
        yy, xx = np.nonzero(reg)
        regions.append({"id": i + 1, "area": area, "cx": float(xx.mean() + sl[1].start), "cy": float(yy.mean() + sl[0].start)})
    return lab, regions


def hatch_mask(gray_view, win=13, thr=0.5):
    """Заштрихованные/тёмные области (сетки решёток, воздухозаборники, диффузор)."""
    ink = (gray_view < 170).astype(float)
    m = ndi.uniform_filter(ink, win) > thr
    m = ndi.binary_opening(m, iterations=2)
    m = ndi.binary_dilation(m, iterations=3)
    return m


def lamp_mask(gray_view, ground, roof, band, side_frac=(0.16, 0.48), max_area=4000):
    """Фары/фонари: мелкие замкнутые области в полосе высот band (доли от земли до крыши) по бокам."""
    lab, regs = enclosed_regions(gray_view, min_area=30, max_area=max_area)
    H, W = gray_view.shape
    cx = W / 2
    m = np.zeros((H, W), bool)
    for r in regs:
        hfrac = (ground - r["cy"]) / max(ground - roof, 1)
        dx = abs(r["cx"] - cx) / W
        if band[0] <= hfrac <= band[1] and side_frac[0] <= dx <= side_frac[1]:
            m |= lab == r["id"]
    # каждая сторона — выпуклая оболочка
    from skimage.morphology import convex_hull_image
    out = np.zeros_like(m)
    for half in (slice(0, W // 2), slice(W // 2, W)):
        sub = m[:, half]
        if sub.sum() > 20:
            out[:, half] = convex_hull_image(sub)
    return out


def side_glass_mask(gray_view, ground, roof_row, x0, x1, belt_frac=0.70):
    lab, regs = enclosed_regions(gray_view, min_area=300, max_area=40000)
    m = np.zeros(gray_view.shape, bool)
    for r in regs:
        hfrac = (ground - r["cy"]) / max(ground - roof_row, 1)
        if hfrac >= belt_frac and x0 <= r["cx"] <= x1:
            m |= lab == r["id"]
    return ndi.binary_dilation(m, iterations=2)
