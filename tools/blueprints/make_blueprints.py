#!/usr/bin/env python3
"""
Чертежи-референсы (blueprints) машин RacingSim для моделирования в Blender.

Для каждой машины рисует 4 ортогональные проекции в ОДНОМ масштабе:
вид сбоку (слева), сверху, спереди, сзади + сводный лист с размерами.
Габариты (длина, ширина, высота, база, колея, свесы, диаметры колёс) берутся
из RacingSim/Assets/Resources/Cars/<id>.json, форма кузова описана ниже
ключевыми точками по мотивам реальных машин (это стилизованные референсы,
а не заводские чертежи — сверяйте с фотографиями).

Выход: art/blueprints/<id>/  side.png top.png front.png back.png sheet.png sheet.svg

Отдельные виды: 400 px на метр, общий центр — середина между осями на земле
(для side/top), продольная ось машины (для front/back). См. README в папке art/blueprints.

Запуск:  python make_blueprints.py
"""
import json
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from matplotlib.patches import Circle, Polygon, Rectangle  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
CARS_DIR = os.path.join(ROOT, "RacingSim", "Assets", "Resources", "Cars")
OUT_DIR = os.path.join(ROOT, "art", "blueprints")

PX_PER_M = 400
LINE = "#1b1f24"
GRID = "#d5dbe3"
GLASS = "#9fb3c8"
DARK = "#2a2e33"
TYRE = "#3a3a3a"
RIM = "#b9bec4"

# ---------------------------------------------------------------------------
# Форма кузова. s — расстояние от носа назад (м), y — высота от земли (м),
# w — полуширина (м). Точки сглаживаются сплайном Катмулла — Рома.
# ---------------------------------------------------------------------------
SHAPES = {
    "AstonMartinVantageGT3": {
        "label": "Aston Martin Vantage AMR GT3 Evo",
        "upper": [(0.00, 0.17), (-0.01, 0.30), (0.03, 0.48), (0.16, 0.60), (0.55, 0.70), (1.05, 0.79),
                  (1.45, 0.81), (1.85, 0.86), (2.25, 1.06), (2.60, 1.20), (2.95, 1.22), (3.25, 1.18),
                  (3.70, 1.06), (4.10, 0.97), (4.45, 0.955), (4.63, 0.975), (4.70, 0.93), (4.71, 0.70),
                  (4.68, 0.45), (4.60, 0.30)],
        "lower": [(4.55, 0.24), (4.30, 0.17), (3.40, 0.12), (1.60, 0.12), (0.50, 0.12), (0.10, 0.13)],
        "glass": [(1.97, 0.91), (2.30, 1.06), (2.62, 1.16), (2.95, 1.175), (3.25, 1.14), (3.70, 1.02),
                  (3.55, 0.96), (2.40, 0.93)],
        "windshield": (1.85, 0.86, 2.60, 1.20),
        "plan": [(0.00, 0.60), (0.08, 0.84), (0.35, 0.97), (1.05, 1.025), (1.65, 0.98), (2.35, 0.93),
                 (3.05, 0.97), (3.755, 1.025), (4.30, 0.99), (4.62, 0.90), (4.70, 0.78)],
        "plan_glass": [(1.85, 0.60), (2.55, 0.66), (3.20, 0.64), (4.05, 0.52)],
        "section_front": {"shoulder": 0.80, "glass_w": 0.64, "roof_w": 0.48, "nose_w": 0.94},
        "section_rear": {"shoulder": 0.95, "glass_w": 0.58, "roof_w": 0.48, "tail_w": 0.92},
        "wing": {"s0": 4.24, "chord": 0.42, "y": 1.25, "span": 1.86, "mount": "swan", "deck_y": 0.96},
        "headlight": [(0.10, 0.56), (0.42, 0.63), (0.62, 0.66), (0.40, 0.59), (0.13, 0.52)],
        "taillight": [(4.55, 0.93), (4.70, 0.91), (4.70, 0.86), (4.55, 0.89)],
        "vents": [(1.48, 0.50, 1.78, 0.72)],  # жабры за передним колесом
        "side_intake": None,
        "doors": [1.92, 3.15],
        "mirror_s": 2.02,
        "grille": "aston",
        "front_lights": "swept",
        "notes": ["Длинный капот, кабина сдвинута назад (передне-средний мотор)",
                  "Решётка Aston Martin во всю ширину носа, большой сплиттер",
                  "Фастбэк с «утиным хвостом», антикрыло на креплениях swan-neck",
                  "Жабры на передних крыльях, расширенные арки"],
    },
    "Ferrari296GT3": {
        "label": "Ferrari 296 GT3",
        "upper": [(0.00, 0.16), (-0.01, 0.30), (0.10, 0.44), (0.40, 0.56), (0.75, 0.66), (0.95, 0.70),
                  (1.20, 0.73), (1.40, 0.79), (1.75, 1.00), (2.05, 1.13), (2.35, 1.15), (2.65, 1.12),
                  (3.00, 1.02), (3.35, 0.95), (3.61, 0.93), (4.10, 0.92), (4.55, 0.91), (4.70, 0.86),
                  (4.72, 0.60), (4.68, 0.40), (4.58, 0.27)],
        "lower": [(4.50, 0.22), (4.10, 0.15), (3.00, 0.11), (1.50, 0.11), (0.40, 0.11), (0.08, 0.12)],
        "glass": [(1.50, 0.84), (1.80, 0.99), (2.05, 1.09), (2.40, 1.11), (2.70, 1.07), (2.95, 0.97),
                  (2.70, 0.90), (1.85, 0.86)],
        "windshield": (1.40, 0.79, 2.05, 1.13),
        "plan": [(0.00, 0.52), (0.10, 0.78), (0.45, 0.97), (0.95, 1.025), (1.60, 0.96), (2.30, 0.91),
                 (3.00, 0.98), (3.61, 1.025), (4.30, 1.00), (4.62, 0.92), (4.71, 0.84)],
        "plan_glass": [(1.40, 0.58), (2.05, 0.62), (2.65, 0.58), (3.05, 0.40)],
        "section_front": {"shoulder": 0.72, "glass_w": 0.60, "roof_w": 0.44, "nose_w": 0.88},
        "section_rear": {"shoulder": 0.92, "glass_w": 0.50, "roof_w": 0.44, "tail_w": 0.94},
        "wing": {"s0": 4.24, "chord": 0.42, "y": 1.20, "span": 1.84, "mount": "swan", "deck_y": 0.92},
        "headlight": [(0.18, 0.50), (0.55, 0.60), (0.70, 0.63), (0.50, 0.57), (0.20, 0.47)],
        "taillight": [(4.55, 0.90), (4.70, 0.86), (4.71, 0.82), (4.56, 0.86)],
        "vents": [(1.30, 0.50, 1.55, 0.68)],
        "side_intake": [(2.85, 0.45), (3.20, 0.48), (3.25, 0.78), (2.95, 0.72)],  # воздухозаборник перед задним колесом
        "doors": [1.45, 2.85],
        "mirror_s": 1.62,
        "grille": "ferrari",
        "front_lights": "slim",
        "notes": ["Средне-моторная компоновка: короткий низкий нос, кабина сдвинута вперёд",
                  "Каплевидные боковые окна, «аркбутаны» за кабиной",
                  "Большие воздухозаборники перед задними колёсами",
                  "Длинный задний свес с диффузором, антикрыло на креплениях swan-neck"],
    },
    "BMWM4GT3": {
        "label": "BMW M4 GT3",
        "upper": [(0.00, 0.17), (-0.01, 0.32), (0.04, 0.64), (0.25, 0.72), (0.70, 0.78), (1.12, 0.82),
                  (1.55, 0.84), (1.95, 0.87), (2.35, 1.08), (2.70, 1.23), (3.05, 1.25), (3.50, 1.22),
                  (3.85, 1.10), (4.20, 0.98), (4.40, 0.975), (4.90, 0.975), (5.00, 0.95), (5.03, 0.78),
                  (5.00, 0.45), (4.90, 0.29)],
        "lower": [(4.85, 0.23), (4.40, 0.15), (3.50, 0.11), (1.70, 0.11), (0.45, 0.11), (0.08, 0.13)],
        "glass": [(2.03, 0.92), (2.40, 1.07), (2.72, 1.18), (3.10, 1.20), (3.48, 1.18), (3.95, 1.01),
                  (3.70, 0.95), (2.60, 0.93)],
        "windshield": (1.95, 0.87, 2.70, 1.23),
        "plan": [(0.00, 0.66), (0.08, 0.88), (0.45, 0.99), (1.12, 1.02), (1.80, 0.97), (2.60, 0.95),
                 (3.40, 0.97), (4.037, 1.02), (4.60, 0.99), (4.95, 0.92), (5.02, 0.84)],
        "plan_glass": [(1.95, 0.64), (2.70, 0.68), (3.50, 0.66), (4.20, 0.60)],
        "section_front": {"shoulder": 0.84, "glass_w": 0.66, "roof_w": 0.52, "nose_w": 0.96},
        "section_rear": {"shoulder": 0.97, "glass_w": 0.62, "roof_w": 0.52, "tail_w": 0.95},
        "wing": {"s0": 4.48, "chord": 0.45, "y": 1.30, "span": 1.86, "mount": "posts", "deck_y": 0.975},
        "headlight": [(0.06, 0.62), (0.40, 0.68), (0.58, 0.70), (0.40, 0.64), (0.08, 0.58)],
        "taillight": [(4.80, 0.95), (5.00, 0.93), (5.02, 0.86), (4.82, 0.89)],
        "vents": [(1.55, 0.52, 1.88, 0.74)],
        "side_intake": None,
        "doors": [2.00, 3.30],
        "mirror_s": 2.10,
        "grille": "bmw",
        "front_lights": "bmw",
        "notes": ["Классическое купе «три объёма»: капот, кабина, багажник",
                  "Огромные вертикальные «ноздри» BMW в центре бампера",
                  "Самая длинная база (2917 мм) и длина (5.02 м) из трёх",
                  "Антикрыло на стойках, закреплённых на крышке багажника"],
    },
}


# ---------------------------------------------------------------------------
def catmull(points, n=12):
    """Сплайн Катмулла — Рома через все точки (открытая кривая)."""
    p = np.array(points, float)
    if len(p) < 3:
        return p
    ext = np.vstack([2 * p[0] - p[1], p, 2 * p[-1] - p[-2]])
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for t in np.linspace(0, 1, n, endpoint=False):
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(p[-1])
    return np.array(out)


def closed_smooth(points, n=10):
    p = list(points)
    c = catmull(p + p[:3], n)
    return c[: (len(p)) * n]


class Car:
    def __init__(self, spec, shape):
        self.spec, self.shape = spec, shape
        d = spec["dimensions"]
        self.L, self.W, self.H = d["length"], d["width"], d["height"]
        self.wb, self.fo = d["wheelbase"], d["frontOverhang"]
        self.tf, self.tr = d["trackFront"], d["trackRear"]
        self.rf, self.rr = spec["tyreFront"]["radius"], spec["tyreRear"]["radius"]
        self.wf, self.wr = spec["tyreFront"]["width"], spec["tyreRear"]["width"]
        self.sF = self.fo
        self.sR = self.fo + self.wb
        self.mid = self.fo + self.wb / 2  # середина между осями — начало координат видов

    def x(self, s):
        """Горизонтальная координата видов сбоку/сверху: нос слева, 0 — середина базы."""
        return np.asarray(s) - self.mid


# ---------------------------------------------------------------------------
# Виды
# ---------------------------------------------------------------------------
def draw_side(ax, car):
    sh = car.shape
    upper = catmull(sh["upper"], 14)
    lower = catmull(sh["lower"], 10)
    body = np.vstack([upper, lower])
    body_xy = np.column_stack([car.x(body[:, 0]), body[:, 1]])
    color = car.spec.get("bodyColor", [0.5, 0.5, 0.5])
    tint = tuple(0.78 + 0.22 * c for c in color)
    bp = Polygon(body_xy, closed=True, fc=tint, ec=LINE, lw=1.6, zorder=2)
    ax.add_patch(bp)

    # сплиттер и диффузор
    ax.add_patch(Rectangle((car.x(-0.06), 0.065), 0.70, 0.03, fc=DARK, ec=LINE, lw=1, zorder=3))
    ax.add_patch(Rectangle((car.x(car.L - 0.45), 0.10), 0.45, 0.04, fc=DARK, ec=LINE, lw=1, zorder=3))

    # стёкла
    g = closed_smooth(sh["glass"], 8)
    ax.add_patch(Polygon(np.column_stack([car.x(g[:, 0]), g[:, 1]]), fc=GLASS, ec=LINE, lw=1.2, zorder=3))
    # двери
    for s in sh["doors"]:
        ys = np.interp(s, *zip(*sorted((p[0], p[1]) for p in sh["upper"][:12])))
        ax.plot(car.x([s, s + 0.02]), [0.16, min(ys, 0.93)], color=LINE, lw=0.9, zorder=3)
    # зеркало
    ms = sh["mirror_s"]
    ax.add_patch(Polygon([(car.x(ms), 0.93), (car.x(ms + 0.2), 0.95), (car.x(ms + 0.2), 1.03), (car.x(ms + 0.02), 1.02)],
                         fc=tint, ec=LINE, lw=1, zorder=4))
    # фары, фонари, жабры, воздухозаборники
    hl = catmull(sh["headlight"] + [sh["headlight"][0]], 6)
    ax.add_patch(Polygon(np.column_stack([car.x(hl[:, 0]), hl[:, 1]]), fc="#fff6c8", ec=LINE, lw=1, zorder=4))
    tl = np.array(sh["taillight"])
    ax.add_patch(Polygon(np.column_stack([car.x(tl[:, 0]), tl[:, 1]]), fc="#d83a3a", ec=LINE, lw=1, zorder=4))
    for (s0, y0, s1, y1) in sh["vents"]:
        for k in range(4):
            t = k / 3
            ax.plot(car.x([s0 + 0.06 * t, s1 - 0.06 * (1 - t)]), [y0 + (y1 - y0) * t * 0.9, y0 + (y1 - y0) * (0.1 + t * 0.9)],
                    color=LINE, lw=1.2, zorder=4)
    if sh["side_intake"]:
        si = np.array(sh["side_intake"])
        ax.add_patch(Polygon(np.column_stack([car.x(si[:, 0]), si[:, 1]]), fc=DARK, ec=LINE, lw=1, zorder=4))

    # арки и колёса
    for s, r, in ((car.sF, car.rf), (car.sR, car.rr)):
        well = Circle((car.x(s), r), r + 0.055, fc=DARK, ec=LINE, lw=1.2, zorder=3)
        well.set_clip_path(bp)
        ax.add_patch(well)
        ax.add_patch(Circle((car.x(s), r), r, fc=TYRE, ec=LINE, lw=1.2, zorder=5))
        ax.add_patch(Circle((car.x(s), r), r * 0.68, fc=RIM, ec=LINE, lw=1, zorder=6))
        for a in np.linspace(0, 2 * np.pi, 10, endpoint=False):
            ax.plot([car.x(s) + 0.07 * np.cos(a), car.x(s) + r * 0.64 * np.cos(a)],
                    [r + 0.07 * np.sin(a), r + r * 0.64 * np.sin(a)], color="#6b7178", lw=1.4, zorder=7)
        ax.add_patch(Circle((car.x(s), r), 0.06, fc="#d9a400", ec=LINE, lw=1, zorder=8))  # гайка

    # антикрыло
    w = sh["wing"]
    s0, c, y = w["s0"], w["chord"], w["y"]
    # профиль антикрыла: выпуклость снизу (прижим), задняя кромка поднята
    prof = np.array([(0, 0), (0.12, 0.012), (0.6, 0.012), (1.0, 0.0), (0.6, -0.035), (0.15, -0.03)])
    ang = np.radians(10)
    rot = np.array([[np.cos(ang), -np.sin(ang)], [np.sin(ang), np.cos(ang)]])
    pts = (prof * [c, c * 1.3]) @ rot.T + [0, y - c * 0.08]
    # торцевая пластина (позади профиля)
    ax.add_patch(Polygon([(car.x(s0 - 0.04), y - 0.13), (car.x(s0 + c + 0.04), y - 0.10), (car.x(s0 + c + 0.04), y + 0.10),
                          (car.x(s0 + 0.02), y + 0.06)], fc="#cfd5dc", ec=LINE, lw=1, zorder=5))
    ax.add_patch(Polygon(np.column_stack([car.x(s0 + pts[:, 0]), pts[:, 1]]), fc=DARK, ec=LINE, lw=1.2, zorder=6))
    deck = w["deck_y"]
    sm = s0 + c * 0.5
    if w["mount"] == "swan":
        # «лебединая шея»: от крышки вверх, крепится к профилю сверху
        neck = catmull([(sm + 0.08, deck), (sm + 0.02, deck + 0.10), (sm - 0.06, y + 0.03), (sm + 0.04, y + 0.07)], 10)
        ax.plot(car.x(neck[:, 0]), neck[:, 1], color=LINE, lw=3, zorder=7, solid_capstyle="round")
    else:
        # прямые стойки снизу
        ax.plot(car.x([sm - 0.06, sm]), [deck, y - 0.04], color=LINE, lw=3.5, zorder=5)

    # земля
    ax.plot([car.x(-0.4), car.x(car.L + 0.4)], [0, 0], color=LINE, lw=1)


def draw_top(ax, car):
    sh = car.shape
    plan = catmull(sh["plan"], 12)
    xs = car.x(plan[:, 0])
    outline = np.vstack([np.column_stack([xs, plan[:, 1]]), np.column_stack([xs[::-1], -plan[::-1, 1]])])
    color = car.spec.get("bodyColor", [0.5, 0.5, 0.5])
    tint = tuple(0.78 + 0.22 * c for c in color)
    # колёса под кузовом (пунктиром)
    for s, r, t, w in ((car.sF, car.rf, car.tf, car.wf), (car.sR, car.rr, car.tr, car.wr)):
        for side in (-1, 1):
            ax.add_patch(Rectangle((car.x(s) - r, side * t / 2 - w / 2), 2 * r, w, fc=TYRE, ec=LINE, lw=1, zorder=1))
    ax.add_patch(Polygon(outline, closed=True, fc=tint, ec=LINE, lw=1.6, zorder=2, alpha=0.93))
    # сплиттер
    ax.add_patch(Rectangle((car.x(-0.06), -sh["plan"][2][1] * 0.95), 0.10, 1.9 * sh["plan"][2][1], fc="#8a939c", ec=LINE, lw=1, zorder=1))
    # стекло/крыша
    pg = catmull(sh["plan_glass"], 10)
    gx = car.x(pg[:, 0])
    glass = np.vstack([np.column_stack([gx, pg[:, 1]]), np.column_stack([gx[::-1], -pg[::-1, 1]])])
    ax.add_patch(Polygon(glass, closed=True, fc=GLASS, ec=LINE, lw=1.2, zorder=3))
    ws = sh["windshield"]
    roof0, roof1 = ws[2], sh["glass"][4][0]
    rw = sh["section_front"]["roof_w"]
    ax.add_patch(Rectangle((car.x(roof0), -rw), roof1 - roof0, 2 * rw, fc=tint, ec=LINE, lw=1.2, zorder=4))
    # зеркала
    ms = sh["mirror_s"]
    for side in (-1, 1):
        y0 = np.interp(ms, plan[:, 0], plan[:, 1]) - 0.08
        ax.add_patch(Polygon([(car.x(ms), side * y0), (car.x(ms + 0.18), side * y0), (car.x(ms + 0.15), side * (y0 + 0.17)),
                              (car.x(ms + 0.03), side * (y0 + 0.17))], fc=tint, ec=LINE, lw=1, zorder=4))
    # вентиляция капота / крышки мотора
    hood = (0.55, 1.35) if sh["grille"] != "ferrari" else (0.35, 0.9)
    for side in (-1, 1):
        ax.add_patch(Rectangle((car.x(hood[0]), side * 0.18 - 0.11), hood[1] - hood[0], 0.22, fc="none", ec=LINE, lw=1, zorder=4))
        for k in range(1, 6):
            xk = car.x(hood[0] + (hood[1] - hood[0]) * k / 6)
            ax.plot([xk, xk], [side * 0.18 - 0.1, side * 0.18 + 0.1], color=LINE, lw=0.7, zorder=4)
    if sh["grille"] == "ferrari":  # жалюзи крышки двигателя
        for k in range(8):
            xk = car.x(3.25 + k * 0.1)
            ax.plot([xk, xk], [-0.35, 0.35], color=LINE, lw=0.8, zorder=4)
    # антикрыло
    w = sh["wing"]
    ax.add_patch(Rectangle((car.x(w["s0"]), -w["span"] / 2), w["chord"], w["span"], fc=DARK, ec=LINE, lw=1.2, zorder=5, alpha=0.9))
    for side in (-1, 1):
        ax.add_patch(Rectangle((car.x(w["s0"] - 0.03), side * w["span"] / 2 - 0.01), w["chord"] + 0.06, 0.02, fc=LINE, zorder=6))
    ax.axhline(0, color="#7a8796", lw=0.6, ls="-.", zorder=7)


def section(car, front=True):
    """Поперечное сечение кузова (половина) для видов спереди/сзади."""
    sec = car.shape["section_front" if front else "section_rear"]
    hw = car.W / 2
    end_w = sec["nose_w"] if front else sec["tail_w"]
    half = [(end_w * 0.9, 0.08), (hw, 0.16), (hw * 1.005, 0.42), (hw * 0.99, 0.62), (hw * 0.93, sec["shoulder"] - 0.04),
            (hw * 0.78, sec["shoulder"] + 0.04), (sec["glass_w"], sec["shoulder"] + 0.08),
            (sec["roof_w"] + 0.05, car.H - 0.03), (sec["roof_w"] * 0.8, car.H), (0.0, car.H)]
    return half


def draw_end(ax, car, front=True):
    sh = car.shape
    half = section(car, front)
    pts = half + [(-x, y) for x, y in reversed(half)]
    sm = closed_smooth(pts, 8)
    color = car.spec.get("bodyColor", [0.5, 0.5, 0.5])
    tint = tuple(0.78 + 0.22 * c for c in color)
    track = car.tf if front else car.tr
    r = car.rf if front else car.rr
    tw = car.wf if front else car.wr
    for side in (-1, 1):
        ax.add_patch(Rectangle((side * track / 2 - tw / 2, 0), tw, 2 * r, fc=TYRE, ec=LINE, lw=1.2, zorder=1))
    ax.add_patch(Polygon(sm, closed=True, fc=tint, ec=LINE, lw=1.6, zorder=2))
    # стекло
    sec = sh["section_front" if front else "section_rear"]
    g = [(sec["glass_w"] - 0.03, sec["shoulder"] + 0.11), (sec["roof_w"] + 0.02, car.H - 0.07),
         (-sec["roof_w"] - 0.02, car.H - 0.07), (-sec["glass_w"] + 0.03, sec["shoulder"] + 0.11)]
    ax.add_patch(Polygon(g, closed=True, fc=GLASS, ec=LINE, lw=1.2, zorder=3))
    # колёсные ниши
    for side in (-1, 1):
        ax.add_patch(Rectangle((side * track / 2 - tw / 2 - 0.02, 0.1), tw + 0.04, 0.32, fc=DARK, ec=LINE, lw=1, zorder=3))
    # зеркала
    for side in (-1, 1):
        ax.add_patch(Polygon([(side * (sec["glass_w"] + 0.08), 0.94), (side * (car.W / 2 + 0.06), 0.95),
                              (side * (car.W / 2 + 0.06), 1.03), (side * (sec["glass_w"] + 0.1), 1.03)], fc=tint, ec=LINE, lw=1, zorder=4))
    w = sh["wing"]
    if front:
        ax.add_patch(Rectangle((-car.W / 2 + 0.04, 0.065), car.W - 0.08, 0.03, fc=DARK, ec=LINE, lw=1, zorder=4))  # сплиттер
        g = sh["grille"]
        if g == "aston":
            grille = [(-0.55, 0.20), (0.55, 0.20), (0.48, 0.48), (-0.48, 0.48)]
            ax.add_patch(Polygon(grille, fc=DARK, ec=LINE, lw=1.2, zorder=4))
            for k in range(1, 8):
                ax.plot([-0.5 + k * 0.125, -0.47 + k * 0.118], [0.21, 0.47], color="#555", lw=0.7, zorder=5)
            lights = [(0.56, 0.55), (0.88, 0.60), (0.86, 0.66), (0.58, 0.61)]
        elif g == "ferrari":
            ax.add_patch(Polygon([(-0.62, 0.15), (0.62, 0.15), (0.55, 0.32), (-0.55, 0.32)], fc=DARK, ec=LINE, lw=1.2, zorder=4))
            ax.add_patch(Polygon([(-0.22, 0.44), (0.22, 0.44), (0.18, 0.56), (-0.18, 0.56)], fc=DARK, ec=LINE, lw=1, zorder=4))  # S-duct
            lights = [(0.52, 0.50), (0.86, 0.54), (0.84, 0.58), (0.54, 0.55)]
        else:  # bmw — огромные «ноздри»
            for side in (-1, 1):
                kid = closed_smooth([(side * 0.04, 0.24), (side * 0.32, 0.24), (side * 0.34, 0.45), (side * 0.30, 0.68),
                                     (side * 0.06, 0.68), (side * 0.03, 0.45)], 6)
                ax.add_patch(Polygon(kid, fc=DARK, ec=LINE, lw=1.6, zorder=4))
                for k in range(1, 7):
                    xk = side * (0.04 + 0.28 * k / 7)
                    ax.plot([xk, xk], [0.26, 0.66], color="#666", lw=0.8, zorder=5)
            ax.add_patch(Polygon([(-0.9, 0.14), (-0.45, 0.14), (-0.45, 0.22), (-0.9, 0.24)], fc=DARK, ec=LINE, lw=1, zorder=4))
            ax.add_patch(Polygon([(0.9, 0.14), (0.45, 0.14), (0.45, 0.22), (0.9, 0.24)], fc=DARK, ec=LINE, lw=1, zorder=4))
            lights = [(0.42, 0.62), (0.86, 0.66), (0.86, 0.72), (0.42, 0.70)]
        for side in (-1, 1):
            ax.add_patch(Polygon([(side * x, y) for x, y in lights], fc="#fff6c8", ec=LINE, lw=1, zorder=5))
    else:
        # фонари, диффузор, выхлоп
        for side in (-1, 1):
            ax.add_patch(Polygon([(side * 0.35, 0.86), (side * 0.92, 0.84), (side * 0.92, 0.90), (side * 0.40, 0.92)],
                                 fc="#d83a3a", ec=LINE, lw=1, zorder=4))
        ax.add_patch(Rectangle((-0.75, 0.08), 1.5, 0.22, fc=DARK, ec=LINE, lw=1.2, zorder=4))
        for k in range(-3, 4):
            ax.plot([k * 0.2, k * 0.2], [0.08, 0.30], color="#777", lw=1, zorder=5)
        for side in (-1, 1):
            ax.add_patch(Circle((side * 0.55, 0.36), 0.05, fc="#555", ec=LINE, lw=1, zorder=5))
    # антикрыло и пластины (видно над крышей)
    ax.add_patch(Rectangle((-w["span"] / 2, w["y"] - 0.02), w["span"], 0.05, fc=DARK, ec=LINE, lw=1.2, zorder=6))
    for side in (-1, 1):
        ax.add_patch(Rectangle((side * w["span"] / 2 - 0.01, w["y"] - 0.17), 0.02, 0.27, fc=LINE, zorder=7))
        if w["mount"] == "swan":
            ax.plot([side * 0.30, side * 0.30], [w["deck_y"], w["y"] + 0.06], color=LINE, lw=3, zorder=5)
        else:
            ax.plot([side * 0.35, side * 0.35], [w["deck_y"], w["y"] - 0.02], color=LINE, lw=3.5, zorder=5)
    ax.axvline(0, color="#7a8796", lw=0.6, ls="-.", zorder=8)
    ax.plot([-1.5, 1.5], [0, 0], color=LINE, lw=1)


# ---------------------------------------------------------------------------
def setup_axes(ax, xlim, ylim, grid=0.25):
    ax.set_xlim(*xlim)
    ax.set_ylim(*ylim)
    ax.set_aspect("equal")
    ax.set_xticks(np.arange(np.ceil(xlim[0] / grid) * grid, xlim[1] + 1e-6, grid), minor=True)
    ax.set_yticks(np.arange(np.ceil(ylim[0] / grid) * grid, ylim[1] + 1e-6, grid), minor=True)
    ax.set_xticks(np.arange(np.ceil(xlim[0]), xlim[1] + 1e-6, 1.0))
    ax.set_yticks(np.arange(np.ceil(ylim[0]), ylim[1] + 1e-6, 1.0))
    ax.grid(which="minor", color=GRID, lw=0.5)
    ax.grid(which="major", color="#b3bdc9", lw=0.9)
    ax.set_axisbelow(True)
    ax.tick_params(labelsize=7, colors="#6b7684")


def dim(ax, x0, x1, y, text, vertical=False):
    kw = dict(arrowprops=dict(arrowstyle="<->", color="#c0392b", lw=1), annotation_clip=False)
    if vertical:
        ax.annotate("", (y, x0), (y, x1), **kw)
        ax.text(y + 0.04, (x0 + x1) / 2, text, color="#c0392b", fontsize=8, rotation=90, va="center", ha="left")
    else:
        ax.annotate("", (x0, y), (x1, y), **kw)
        ax.text((x0 + x1) / 2, y + 0.03, text, color="#c0392b", fontsize=8, ha="center", va="bottom")


VIEWS = {
    # имя: (функция, xlim, ylim)
    "side": (lambda ax, c: draw_side(ax, c), (-3.0, 3.0), (-0.2, 1.8)),
    "top": (lambda ax, c: draw_top(ax, c), (-3.0, 3.0), (-1.5, 1.5)),
    "front": (lambda ax, c: draw_end(ax, c, True), (-1.5, 1.5), (-0.2, 1.8)),
    "back": (lambda ax, c: draw_end(ax, c, False), (-1.5, 1.5), (-0.2, 1.8)),
}


def single_view(car, name, out):
    fn, xl, yl = VIEWS[name]
    wpx, hpx = (xl[1] - xl[0]) * PX_PER_M, (yl[1] - yl[0]) * PX_PER_M
    fig = plt.figure(figsize=(wpx / 100, hpx / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    setup_axes(ax, xl, yl)
    ax.set_xticks([]), ax.set_yticks([])
    ax.set_xticks([], minor=True), ax.set_yticks([], minor=True)
    for s in ax.spines.values():
        s.set_visible(False)
    fn(ax, car)
    fig.savefig(out, dpi=100, facecolor="white")
    plt.close(fig)


def sheet(car, out_png, out_svg):
    fig = plt.figure(figsize=(16.5, 11.7))  # A3
    fig.patch.set_facecolor("white")
    ax_side = fig.add_axes([0.04, 0.53, 0.56, 0.38])
    ax_top = fig.add_axes([0.04, 0.06, 0.56, 0.43])
    ax_front = fig.add_axes([0.62, 0.53, 0.17, 0.38])
    ax_back = fig.add_axes([0.81, 0.53, 0.17, 0.38])
    ax_info = fig.add_axes([0.63, 0.06, 0.35, 0.43])
    for ax, name, title in ((ax_side, "side", "ВИД СБОКУ (слева)"), (ax_top, "top", "ВИД СВЕРХУ"),
                            (ax_front, "front", "ВИД СПЕРЕДИ"), (ax_back, "back", "ВИД СЗАДИ")):
        fn, xl, yl = VIEWS[name]
        setup_axes(ax, xl, yl)
        fn(ax, car)
        ax.set_title(title, fontsize=10, loc="left", color="#33404d", fontweight="bold")
    # размеры
    nose, tail = car.x(0), car.x(car.L)
    dim(ax_side, nose, tail, 1.55, f"Длина {car.L * 1000:.0f} мм")
    dim(ax_side, car.x(car.sF), car.x(car.sR), -0.12, f"База {car.wb * 1000:.0f} мм")
    dim(ax_side, nose, car.x(car.sF), -0.12, f"{car.fo * 1000:.0f}")
    dim(ax_side, car.x(car.sR), tail, -0.12, f"{(car.L - car.sR) * 1000:.0f}")
    dim(ax_side, 0, car.H, tail + 0.25, f"Высота {car.H * 1000:.0f} мм", vertical=True)
    dim(ax_top, -car.W / 2, car.W / 2, tail + 0.25, f"Ширина {car.W * 1000:.0f} мм", vertical=True)
    dim(ax_front, -car.tf / 2, car.tf / 2, -0.15, f"Колея {car.tf * 1000:.0f}")
    dim(ax_back, -car.tr / 2, car.tr / 2, -0.15, f"Колея {car.tr * 1000:.0f}")

    ax_info.axis("off")
    sp = car.spec
    lines = [
        (car.shape["label"], 17, "bold"),
        ("Чертёж-референс для 3D-моделирования (RacingSim)", 10, "normal"),
        ("", 6, "normal"),
        (f"Длина × ширина × высота: {car.L * 1000:.0f} × {car.W * 1000:.0f} × {car.H * 1000:.0f} мм", 10, "normal"),
        (f"База: {car.wb * 1000:.0f} мм   Свесы: перед {car.fo * 1000:.0f} / зад {(car.L - car.sR) * 1000:.0f} мм", 10, "normal"),
        (f"Колея: перед {car.tf * 1000:.0f} / зад {car.tr * 1000:.0f} мм", 10, "normal"),
        (f"Шины: перед {sp['tyreFront']['size']} (Ø{car.rf * 2000:.0f}), зад {sp['tyreRear']['size']} (Ø{car.rr * 2000:.0f})", 10, "normal"),
        (f"Мотор: {sp['engine']['layout']}   Масса: {sp['mass']['mass']:.0f} кг", 10, "normal"),
        ("", 6, "normal"),
        ("Характерные черты:", 10, "bold"),
    ] + [("• " + n, 9.5, "normal") for n in car.shape["notes"]] + [
        ("", 6, "normal"),
        ("Сетка: мелкая 0.25 м, крупная 1 м. Все виды в одном масштабе.", 9, "normal"),
        ("0 по горизонтали — середина базы (сбоку/сверху), ось машины (спереди/сзади).", 9, "normal"),
        ("Габариты — из Resources/Cars/<id>.json; форма — стилизация по фото,", 9, "normal"),
        ("не заводской чертёж: сверяйте детали с фотографиями машины.", 9, "normal"),
    ]
    y = 0.98
    for text, size, weight in lines:
        ax_info.text(0.0, y, text, fontsize=size, fontweight=weight, va="top", color="#1b1f24", transform=ax_info.transAxes)
        y -= 0.012 + size * 0.0072
    fig.savefig(out_png, dpi=110, facecolor="white")
    fig.savefig(out_svg, facecolor="white")
    plt.close(fig)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for cid, shape in SHAPES.items():
        with open(os.path.join(CARS_DIR, cid + ".json"), encoding="utf-8") as f:
            spec = json.load(f)
        car = Car(spec, shape)
        out = os.path.join(OUT_DIR, cid)
        os.makedirs(out, exist_ok=True)
        for v in VIEWS:
            single_view(car, v, os.path.join(out, f"{v}.png"))
        sheet(car, os.path.join(out, "sheet.png"), os.path.join(out, "sheet.svg"))
        print("ok:", out)


if __name__ == "__main__":
    main()
