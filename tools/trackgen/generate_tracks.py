#!/usr/bin/env python3
"""
Генератор центральной линии трасс для RacingSim.

Трасса описывается последовательностью сегментов (прямые и дуги) + профилем
высот + шириной/виражом по ключевым точкам. Скрипт:
  1. замыкает трассу (подбирает длины двух прямых и угол одного поворота),
  2. сглаживает кривизну (плавные входы в повороты, как клотоиды),
  3. масштабирует под реальную длину круга,
  4. накладывает рельеф (перепады высот) и вираж,
  5. сохраняет JSON для Unity (RacingSim/Assets/Resources/Tracks/*.json).

Это приближение реальной геометрии "по памяти" (последовательность поворотов,
длины, перепады высот). Для точной геометрии используйте fetch_osm_track.py
(OpenStreetMap + SRTM), его вывод имеет тот же формат.

Запуск:  python generate_tracks.py [--plot]
"""
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "..", "RacingSim", "Assets", "Resources", "Tracks"))

DS = 1.0  # шаг дискретизации при построении, м
OUT_STEP = 2.0  # шаг точек в выходном файле, м

# ---------------------------------------------------------------------------
# Описание трасс.
# ("S", длина[, "A"|"B"])         — прямая; "A"/"B" = длина подбирается для замыкания
# ("C", угол°, радиус[, "name"])   — дуга; угол > 0 = поворот направо
# ("C", None, радиус, "name")      — угол подбирается, чтобы сумма поворотов = ±360°
# elevation: [(дистанция_м_от_старта_по_реальной_длине, высота_м), ...]
# bank: вираж в градусах, > 0 — правая кромка ниже; runoff: зона безопасности до стены, м
# ---------------------------------------------------------------------------
TRACKS = {
    "Monza": {
        "displayName": "Autodromo Nazionale Monza",
        "country": "Italy",
        "length": 5793.0,
        "direction": 1,  # по часовой
        "baseAltitude": 162.0,
        "segments": [
            ("S", 600, "A"),
            ("C", 80, 16, "Variante del Rettifilo"), ("S", 25), ("C", -95, 18), ("S", 40), ("C", 15, 60),
            ("S", 200),
            ("C", 70, 300, "Curva Biassono"),
            ("S", 500, "B"),
            ("C", -55, 20, "Variante della Roggia"), ("S", 15), ("C", 60, 25),
            ("S", 230),
            ("C", 75, 65, "Lesmo 1"),
            ("S", 180),
            ("C", 70, 50, "Lesmo 2"),
            ("S", 400), ("C", -15, 800, "Serraglio"), ("S", 350),
            ("C", -45, 60, "Variante Ascari"), ("S", 40), ("C", 75, 55), ("S", 40), ("C", -35, 90),
            ("S", 1000),
            ("C", 70, 105, "Curva Alboreto (Parabolica)"), ("C", None, 170), ("C", 40, 260),
            ("S", 450),
        ],
        "elevation": [(0, 0), (600, -1), (1500, 2), (2300, 6), (2800, 9), (3200, 8),
                      (3900, 5), (4500, 3), (5100, 1), (5793, 0)],
        "width": [(0, 15), (500, 13), (700, 12), (5500, 14)],
        "bank": [],  # (дистанция, градусы) — у Монцы вираж почти нулевой
        "runoff": [(0, 18)],  # ширина зоны безопасности до отбойника, м
        "trees": 0.8,         # плотность деревьев (парк Монцы)
    },
    "Spa": {
        "displayName": "Circuit de Spa-Francorchamps",
        "country": "Belgium",
        "length": 7004.0,
        "direction": 1,
        "baseAltitude": 400.0,
        "segments": [
            ("S", 200, "A"),
            ("C", 165, 20, "La Source"),
            ("S", 450),
            ("C", -30, 120, "Eau Rouge"), ("C", 55, 110, "Raidillon"), ("C", -20, 150),
            ("S", 600), ("C", 10, 1000, "Kemmel"), ("S", 400),
            ("C", 70, 45, "Les Combes"), ("S", 30), ("C", -60, 45), ("C", 45, 60, "Malmedy"),
            ("S", 200),
            ("C", 155, 25, "Rivage"),
            ("S", 250, "B"),
            ("C", -70, 60, "Bruxelles"),
            ("S", 280),
            ("C", -60, 110, "Pouhon"), ("S", 60), ("C", -60, 110),
            ("S", 450),
            ("C", 60, 40, "Fagnes"), ("S", 40), ("C", -55, 40),
            ("S", 200),
            ("C", 60, 80, "Paul Frere"), ("S", 150), ("C", None, 90, "Stavelot"),
            ("S", 300), ("C", 15, 600), ("S", 600),
            ("C", -35, 300, "Blanchimont"), ("S", 100), ("C", -25, 250),
            ("S", 600),
            ("C", 70, 22, "Bus Stop"), ("S", 20), ("C", -50, 22),
            ("S", 80),
        ],
        "elevation": [(0, 0), (230, 2), (700, -20), (950, 16), (2000, 40), (2500, 12),
                      (2800, 2), (3300, -25), (3900, -42), (4500, -62), (5000, -58),
                      (5700, -40), (6600, -10), (7004, 0)],
        "width": [(0, 14), (300, 11), (6800, 12)],
        "bank": [(800, 0), (900, 4), (1000, 0)],
        "runoff": [(0, 16)],
        "trees": 1.0,  # Арденнский лес
    },
    "MountPanorama": {
        "displayName": "Mount Panorama Circuit (Bathurst)",
        "country": "Australia",
        "length": 6213.0,
        "direction": -1,  # против часовой
        "baseAltitude": 690.0,
        "segments": [
            ("S", 300, "A"),
            ("C", -95, 30, "Hell Corner"),
            ("S", 450, "B"), ("C", 10, 400, "Mountain Straight"), ("S", 450),
            ("C", 80, 45, "Griffins Bend"),
            ("S", 200),
            ("C", -110, 25, "The Cutting"),
            ("S", 150),
            ("C", -40, 70, "Reid Park"),
            ("S", 200),
            ("C", 40, 90, "Sulman Park"),
            ("S", 150),
            ("C", -60, 90, "McPhillamy Park"),
            ("S", 150),
            ("C", -45, 50, "Skyline"),
            ("C", 60, 40, "The Esses"), ("C", -55, 40, "The Dipper"),
            ("S", 150),
            ("C", None, 25, "Forrest's Elbow"),
            ("S", 600), ("C", 10, 800, "Conrod Straight"), ("S", 400),
            ("C", 25, 250), ("S", 100),
            ("C", -40, 50, "The Chase"), ("S", 40), ("C", 30, 80),
            ("S", 300),
            ("C", -100, 20, "Murray's Corner"),
            ("S", 200),
        ],
        "elevation": [(0, 0), (350, 0), (900, 15), (1400, 45), (1800, 90), (2100, 120),
                      (2550, 160), (2900, 174), (3050, 152), (3250, 112), (3650, 80),
                      (4500, 35), (5300, 8), (5900, 0), (6213, 0)],
        "width": [(0, 15), (400, 11), (1500, 9), (3700, 10), (5900, 13)],
        "bank": [(1650, 0), (1750, 3), (1850, 0), (3150, 0), (3250, -4), (3350, 0)],
        # на горе бетонные стены почти вплотную к трассе
        "runoff": [(0, 14), (1400, 14), (1500, 2.5), (3700, 2.5), (3800, 12), (6213, 14)],
        "trees": 0.6,
    },
}


def integrate_segments(segs):
    """Конечная точка ломаной из сегментов (дуги — аналитически)."""
    x = y = 0.0
    heading = 0.0  # рад, 0 = +Y (север), положительный угол = направо
    for s in segs:
        if s[0] == "S":
            x += s[1] * math.sin(heading)
            y += s[1] * math.cos(heading)
        else:
            a = math.radians(s[1])
            r = s[2]
            # хорда дуги
            chord = 2 * r * math.sin(abs(a) / 2)
            mid = heading + a / 2
            x += chord * math.sin(mid)
            y += chord * math.cos(mid)
            heading += a
    return x, y


def solve_closure(track):
    """
    Минимально «подгибает» трассу, чтобы она замкнулась: меняет длины прямых и углы
    поворотов как можно меньше (взвешенные наименьшие квадраты с ограничениями).
    Прямые с меткой "A"/"B" и поворот с углом None считаются свободнее остальных.
    """
    from scipy.optimize import minimize

    segs = [list(s) for s in track["segments"]]
    target = 360.0 * track["direction"]
    known = sum(s[1] for s in segs if s[0] == "C" and s[1] is not None)
    for s in segs:
        if s[0] == "C" and s[1] is None:
            s[1] = target - known
            s.append(True)  # признак «свободного» угла
    vars_idx, x0, scale, lo = [], [], [], []
    for i, s in enumerate(segs):
        if s[0] == "S":
            free = len(s) > 2 and s[2] in ("A", "B")
            vars_idx.append(i)
            x0.append(s[1])
            scale.append(max(s[1], 100.0) * (3.0 if free else 0.35))
            lo.append((max(20.0, s[1] * 0.3), None))
        else:
            free = len(s) > 4 and s[4] is True
            vars_idx.append(i)
            x0.append(s[1])
            tol = max(8.0, abs(s[1]) * 0.15) * (3.0 if free else 1.0)
            scale.append(tol)
            sign = 1 if s[1] >= 0 else -1
            lo.append((0.2 * s[1], 2.0 * s[1]) if sign > 0 else (2.0 * s[1], 0.2 * s[1]))
    x0 = np.array(x0, float)
    scale = np.array(scale, float)

    def apply(v):
        out = [list(s) for s in segs]
        for k, i in enumerate(vars_idx):
            out[i][1] = float(v[k])
        return out

    def obj(v):
        return float(np.sum(((v - x0) / scale) ** 2))

    cons = [
        {"type": "eq", "fun": lambda v: np.array(integrate_segments(apply(v))) / 100.0},
        {"type": "eq", "fun": lambda v: (sum(v[k] for k, i in enumerate(vars_idx) if segs[i][0] == "C") - target) / 10.0},
    ]
    res = minimize(obj, x0, bounds=lo, constraints=cons, method="SLSQP", options={"maxiter": 500, "ftol": 1e-10})
    if not res.success:
        raise RuntimeError(f"не удалось замкнуть трассу: {res.message}")
    out = apply(res.x)
    changes = []
    for k, i in enumerate(vars_idx):
        if abs(res.x[k] - x0[k]) > max(5.0, 0.05 * abs(x0[k])):
            label = segs[i][3] if segs[i][0] == "C" and len(segs[i]) > 3 else f"#{i}"
            changes.append(f"{segs[i][0]}{label}:{x0[k]:.0f}->{res.x[k]:.0f}")
    return out, changes


def curvature_profile(segs):
    """Кривизна по дистанции с шагом DS + имена поворотов (дистанция начала)."""
    kappa = []
    corners = []
    for s in segs:
        if s[0] == "S":
            kappa += [0.0] * max(1, int(round(s[1] / DS)))
        else:
            a = math.radians(s[1])
            r = s[2]
            n = max(2, int(round(abs(a) * r / DS)))
            if len(s) > 3 and isinstance(s[3], str):
                corners.append((len(kappa) * DS, s[3]))
            kappa += [a / (n * DS)] * n
    return np.array(kappa), corners


def circular_smooth(arr, sigma_samples):
    if sigma_samples <= 0:
        return arr
    n = len(arr)
    half = int(sigma_samples * 3)
    k = np.exp(-0.5 * (np.arange(-half, half + 1) / sigma_samples) ** 2)
    k /= k.sum()
    padded = np.concatenate([arr[-half:], arr, arr[:half]])
    return np.convolve(padded, k, mode="valid")[:n]


def interp_periodic(keys, length, s, default):
    if not keys:
        return np.full_like(s, default)
    ks = np.array([k[0] for k in keys], dtype=float)
    kv = np.array([k[1] for k in keys], dtype=float)
    # периодическое продолжение
    ks = np.concatenate([ks - length, ks, ks + length])
    kv = np.concatenate([kv, kv, kv])
    order = np.argsort(ks)
    return np.interp(s, ks[order], kv[order])


def build(name, track):
    segs, changes = solve_closure(track)
    kappa, corners = curvature_profile(segs)
    # плавные переходы ~ клотоиды длиной ~25 м
    kappa = circular_smooth(kappa, 8.0 / DS)
    # точное замыкание по направлению
    kappa *= (2 * math.pi * track["direction"]) / (kappa.sum() * DS)
    heading = np.cumsum(kappa) * DS - kappa * DS / 2
    x = np.concatenate([[0.0], np.cumsum(np.sin(heading) * DS)])[:-1]
    y = np.concatenate([[0.0], np.cumsum(np.cos(heading) * DS)])[:-1]
    n = len(x)
    raw_len = n * DS
    # остаточная невязка после сглаживания — распределяем линейно
    ex = x[-1] + math.sin(heading[-1]) * DS
    ey = y[-1] + math.cos(heading[-1]) * DS
    t = np.arange(n) / n
    x -= ex * t
    y -= ey * t
    # масштаб под реальную длину круга
    scale = track["length"] / raw_len
    x *= scale
    y *= scale
    corners = [(d * scale, c) for d, c in corners]

    # пересэмплирование с шагом OUT_STEP по фактической длине дуги
    seg = np.hypot(np.diff(np.append(x, x[0])), np.diff(np.append(y, y[0])))
    s_acc = np.concatenate([[0.0], np.cumsum(seg)])
    L = s_acc[-1]
    m = int(round(L / OUT_STEP))
    s_out = np.linspace(0, L, m, endpoint=False)
    xc = np.interp(s_out, s_acc, np.append(x, x[0]))
    yc = np.interp(s_out, s_acc, np.append(y, y[0]))

    # рельеф: (дистанции заданы для реальной длины) + сглаживание ~40 м
    h = interp_periodic(track["elevation"], track["length"], s_out * track["length"] / L, 0.0)
    h = circular_smooth(h, 20.0 / OUT_STEP)
    w = interp_periodic(track["width"], track["length"], s_out, 12.0)
    w = circular_smooth(w, 10.0 / OUT_STEP)
    ro = interp_periodic(track["runoff"], track["length"], s_out * track["length"] / L, 12.0)
    b = interp_periodic(track["bank"], track["length"], s_out, 0.0) if track["bank"] else np.zeros(m)
    # лёгкий поперечный уклон для водоотвода не нужен в физике — вираж только явный

    # центрируем трассу в начале координат (Unity: X = восток, Z = север, Y = высота)
    cx, cy = (xc.max() + xc.min()) / 2, (yc.max() + yc.min()) / 2
    xc -= cx
    yc -= cy

    # проверка самопересечений: точки, далёкие по дистанции, не должны быть ближе ширины
    pts = np.stack([xc, yc], axis=1)
    min_gap = 1e9
    for i in range(0, m, 5):
        d = np.hypot(*(pts - pts[i]).T)
        idx = np.arange(m)
        far = np.minimum(np.abs(idx - i), m - np.abs(idx - i)) * OUT_STEP > 150
        min_gap = min(min_gap, float(d[far].min()))
    grad = np.diff(np.append(h, h[0])) / OUT_STEP
    data = []
    for i in range(m):
        data += [round(float(xc[i]), 3), round(float(h[i]), 3), round(float(yc[i]), 3),
                 round(float(w[i]), 2), round(float(b[i]), 2), round(float(ro[i]), 1)]
    out = {
        "id": name,
        "displayName": track["displayName"],
        "country": track["country"],
        "length": round(float(L), 1),
        "baseAltitude": track["baseAltitude"],
        "clockwise": track["direction"] > 0,
        "source": "segments-approximation",
        "treeDensity": track["trees"],
        "stride": 6,
        "points": data,  # x, y(высота), z, ширина, вираж°, зона безопасности — повторяется
        "corners": [{"name": c, "distance": round(float(d), 1)} for d, c in corners],
    }
    stats = {
        "length": L,
        "elev_range": float(h.max() - h.min()),
        "max_grad_pct": float(np.abs(grad).max() * 100),
        "extent": (float(xc.max() - xc.min()), float(yc.max() - yc.min())),
        "closure_err": math.hypot(ex, ey),
        "min_gap_m": round(min_gap, 1),
        "adjusted": changes,
    }
    return out, (xc, yc, h), stats


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    plot = "--plot" in sys.argv
    results = {}
    for name, track in TRACKS.items():
        out, geom, stats = build(name, track)
        results[name] = (out, geom)
        path = os.path.join(OUT_DIR, f"{name}.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, separators=(",", ":"))
        print(f"{name}: {stats}")
    if plot:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, axes = plt.subplots(2, len(results), figsize=(6 * len(results), 10))
        for col, (name, (out, (x, z, h))) in enumerate(results.items()):
            ax = axes[0, col]
            sc = ax.scatter(x, z, c=h, s=2, cmap="terrain")
            ax.plot(x[0], z[0], "r^", ms=10)
            ax.plot(x[30], z[30], "k>", ms=6)
            stride = out["stride"]
            pts = out["points"]
            for c in out["corners"]:
                i = int(c["distance"] / OUT_STEP) % (len(pts) // stride)
                ax.annotate(c["name"], (pts[i * stride], pts[i * stride + 2]), fontsize=7)
            ax.set_aspect("equal")
            ax.set_title(name)
            fig.colorbar(sc, ax=ax)
            axes[1, col].plot(np.arange(len(h)) * OUT_STEP, h)
            axes[1, col].set_title(f"{name} elevation (m)")
        png = sys.argv[sys.argv.index("--plot") + 1] if len(sys.argv) > sys.argv.index("--plot") + 1 else "tracks.png"
        fig.savefig(png, dpi=90)
        print("plot:", png)


if __name__ == "__main__":
    main()
