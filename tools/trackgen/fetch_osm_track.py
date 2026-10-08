#!/usr/bin/env python3
"""
Точная геометрия трассы из OpenStreetMap + рельеф из SRTM (30 м).

Запускать на своём компьютере (нужен интернет):
    python fetch_osm_track.py Monza
    python fetch_osm_track.py Spa --start 50.4446,5.9690
    python fetch_osm_track.py MountPanorama --plot

Что делает:
  1. Скачивает через Overpass API все линии highway=raceway вокруг трассы.
  2. Собирает из них граф и находит замкнутый контур, длина которого ближе всего
     к официальной длине круга (отсекает пит-лейн, старые конфигурации, картинг).
  3. Запрашивает высоты точек через OpenTopoData (SRTM 30 м), сглаживает шум.
  4. Сохраняет JSON в том же формате, что и generate_tracks.py
     (RacingSim/Assets/Resources/Tracks/<id>.json) — игра подхватит его автоматически.

Ширина, виражи, зоны безопасности и названия поворотов берутся из generate_tracks.py.
Если что-то пошло не так — просто снова запустите generate_tracks.py, он перезапишет файл.

Зависимости: pip install requests numpy networkx
"""
import argparse
import json
import math
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_tracks as gt  # noqa: E402

OVERPASS = "https://overpass-api.de/api/interpreter"
OPENTOPO = "https://api.opentopodata.org/v1/srtm30m"

# центр трассы и примерная точка стартовой линии (lat, lon)
SITES = {
    "Monza": {"center": (45.6190, 9.2840), "radius_deg": 0.02, "start": (45.6188, 9.2811)},
    "Spa": {"center": (50.4370, 5.9710), "radius_deg": 0.025, "start": (50.4446, 5.9690)},
    "MountPanorama": {"center": (-33.4430, 149.5560), "radius_deg": 0.02, "start": (-33.4486, 149.5583)},
}


def http_post(url, data, retries=4):
    import requests
    for k in range(retries):
        try:
            r = requests.post(url, data=data, timeout=120)
            r.raise_for_status()
            return r.json()
        except Exception as e:  # noqa: BLE001
            print(f"  повтор {k + 1}: {e}")
            time.sleep(2 ** (k + 1))
    raise RuntimeError("Overpass недоступен")


def http_get(url, params, retries=5):
    import requests
    for k in range(retries):
        try:
            r = requests.get(url, params=params, timeout=60)
            if r.status_code == 429:
                time.sleep(2 ** (k + 1))
                continue
            r.raise_for_status()
            return r.json()
        except Exception as e:  # noqa: BLE001
            print(f"  повтор {k + 1}: {e}")
            time.sleep(2 ** (k + 1))
    raise RuntimeError("сервис высот недоступен")


def fetch_ways(site):
    lat, lon = site["center"]
    d = site["radius_deg"]
    q = f"""[out:json][timeout:90];
way["highway"="raceway"]({lat - d},{lon - d * 1.5},{lat + d},{lon + d * 1.5});
out geom;"""
    data = http_post(OVERPASS, {"data": q})
    ways = []
    for el in data.get("elements", []):
        if el.get("type") == "way" and "geometry" in el:
            ways.append({"id": el["id"], "nodes": el["nodes"],
                         "geom": [(g["lat"], g["lon"]) for g in el["geometry"]],
                         "tags": el.get("tags", {})})
    return ways


def to_local(lat, lon, lat0, lon0):
    """Локальные метры: x — восток, z — север."""
    R = 6371000.0
    x = math.radians(lon - lon0) * R * math.cos(math.radians(lat0))
    z = math.radians(lat - lat0) * R
    return x, z


def best_cycle(ways, target_len, lat0, lon0):
    """Граф из линий OSM → замкнутый контур с длиной, ближайшей к target_len."""
    import networkx as nx

    coords = {}
    G = nx.MultiGraph()
    for w in ways:
        pts = [to_local(la, lo, lat0, lon0) for la, lo in w["geom"]]
        for nid, p in zip(w["nodes"], pts):
            coords[nid] = p
        for a, b in zip(w["nodes"][:-1], w["nodes"][1:]):
            if a == b:
                continue
            pa, pb = coords[a], coords[b]
            G.add_edge(a, b, length=math.dist(pa, pb))
    # упрощаем: склеиваем цепочки узлов степени 2 в рёбра с путём
    S = nx.Graph()
    junctions = {n for n in G.nodes if G.degree(n) != 2}
    if not junctions:  # одна замкнутая линия
        cyc = nx.cycle_basis(nx.Graph(G))
        nodes = max(cyc, key=len)
        return [coords[n] for n in nodes]
    visited = set()
    for j in junctions:
        for nb in G.neighbors(j):
            path = [j, nb]
            length = G[j][nb][0]["length"]
            prev, cur = j, nb
            while cur not in junctions:
                nxt = [n for n in G.neighbors(cur) if n != prev]
                if not nxt:
                    break
                prev, cur = cur, nxt[0]
                path.append(cur)
                length += G[prev][cur][0]["length"]
            key = (min(path[0], path[-1]), max(path[0], path[-1]), round(length, 1))
            if key in visited or cur not in junctions:
                continue
            visited.add(key)
            a, b = path[0], path[-1]
            if a == b:
                continue
            # параллельные рёбра (пит-лейн вдоль прямой) — через виртуальный средний узел
            mid = ("mid", len(visited))
            S.add_edge(a, mid, length=length * 0.5, path=path, half=0)
            S.add_edge(mid, b, length=length * 0.5, path=path, half=1)
    best, best_err = None, float("inf")
    for cyc in nx.simple_cycles(S, length_bound=80):
        if len(cyc) < 4:
            continue
        L = sum(S[cyc[i]][cyc[(i + 1) % len(cyc)]]["length"] for i in range(len(cyc)))
        err = abs(L - target_len)
        if err < best_err:
            best, best_err = cyc, err
    if best is None:
        raise RuntimeError("замкнутый контур не найден — проверьте данные OSM")
    print(f"  лучший контур: ошибка длины {best_err:.0f} м")
    # начинаем с реального узла, идём через средние узлы
    k0 = next(i for i, n in enumerate(best) if not (isinstance(n, tuple) and n[0] == "mid"))
    best = best[k0:] + best[:k0]
    pts = []
    for i in range(0, len(best), 2):
        a, b = best[i], best[(i + 2) % len(best)]
        path = S[a][best[i + 1]]["path"]
        if path[0] != a:
            path = path[::-1]
        pts += [coords[n] for n in path[:-1]]
    return pts


def resample_closed(pts, step):
    p = np.array(pts + [pts[0]], float)
    seg = np.hypot(*np.diff(p, axis=0).T)
    s = np.concatenate([[0], np.cumsum(seg)])
    L = s[-1]
    m = int(round(L / step))
    so = np.linspace(0, L, m, endpoint=False)
    return np.interp(so, s, p[:, 0]), np.interp(so, s, p[:, 1]), L


def signed_area(x, z):
    return 0.5 * np.sum(x * np.roll(z, -1) - np.roll(x, -1) * z)


def fetch_elevation(lats, lons):
    out = []
    for i in range(0, len(lats), 100):
        locs = "|".join(f"{la:.6f},{lo:.6f}" for la, lo in zip(lats[i:i + 100], lons[i:i + 100]))
        data = http_get(OPENTOPO, {"locations": locs, "interpolation": "cubic"})
        out += [r["elevation"] if r["elevation"] is not None else np.nan for r in data["results"]]
        time.sleep(1.1)  # бесплатный лимит: 1 запрос/с
        print(f"  высоты: {min(i + 100, len(lats))}/{len(lats)}")
    arr = np.array(out, float)
    if np.isnan(arr).any():
        idx = np.arange(len(arr))
        good = ~np.isnan(arr)
        arr = np.interp(idx, idx[good], arr[good], period=len(arr))
    return arr


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("track", choices=list(SITES))
    ap.add_argument("--start", help="lat,lon стартовой линии (по умолчанию — встроенная)")
    ap.add_argument("--step", type=float, default=gt.OUT_STEP)
    ap.add_argument("--elev-step", type=float, default=10.0, help="шаг запроса высот, м")
    ap.add_argument("--plot", action="store_true")
    args = ap.parse_args()

    site = SITES[args.track]
    spec = gt.TRACKS[args.track]
    lat0, lon0 = site["center"]
    print("Overpass: загрузка линий raceway…")
    ways = fetch_ways(site)
    print(f"  линий: {len(ways)}")
    pts = best_cycle(ways, spec["length"], lat0, lon0)
    x, z, L = resample_closed(pts, args.step)

    # направление движения
    clockwise = signed_area(x, z) < 0  # x — восток, z — север: отрицательная площадь = по часовой
    if clockwise != (spec["direction"] > 0):
        x, z = x[::-1].copy(), z[::-1].copy()
    # старт — ближайшая точка к стартовой линии
    slat, slon = (map(float, args.start.split(","))) if args.start else site["start"]
    sx, sz = to_local(slat, slon, lat0, lon0)
    i0 = int(np.argmin(np.hypot(x - sx, z - sz)))
    x, z = np.roll(x, -i0), np.roll(z, -i0)
    m = len(x)
    print(f"  длина по OSM: {L:.0f} м (официальная {spec['length']:.0f} м), точек: {m}")

    # высоты по разреженной сетке точек, затем интерполяция и сглаживание
    R = 6371000.0
    k = max(1, int(args.elev_step / args.step))
    idx = np.arange(0, m, k)
    lats = lat0 + np.degrees(z[idx] / R)
    lons = lon0 + np.degrees(x[idx] / (R * math.cos(math.radians(lat0))))
    print("OpenTopoData SRTM: высоты…")
    e = fetch_elevation(list(lats), list(lons))
    h = np.interp(np.arange(m), np.append(idx, m), np.append(e, e[0]))
    h = gt.circular_smooth(h, 40.0 / args.step)  # SRTM шумит на ±5 м
    base = float(h[0])
    h -= base

    s_out = np.arange(m) * L / m
    w = gt.circular_smooth(gt.interp_periodic(spec["width"], spec["length"], s_out * spec["length"] / L, 12.0), 10.0 / args.step)
    b = gt.interp_periodic(spec["bank"], spec["length"], s_out * spec["length"] / L, 0.0) if spec["bank"] else np.zeros(m)
    ro = gt.interp_periodic(spec["runoff"], spec["length"], s_out * spec["length"] / L, 12.0)

    cx, cz = (x.max() + x.min()) / 2, (z.max() + z.min()) / 2
    x -= cx
    z -= cz
    data = []
    for i in range(m):
        data += [round(float(x[i]), 3), round(float(h[i]), 3), round(float(z[i]), 3),
                 round(float(w[i]), 2), round(float(b[i]), 2), round(float(ro[i]), 1)]
    # повороты — по доле дистанции из приближённой модели
    approx, _, _ = gt.build(args.track, spec)
    corners = [{"name": c["name"], "distance": round(c["distance"] / approx["length"] * L, 1)} for c in approx["corners"]]
    out = {
        "id": args.track, "displayName": spec["displayName"], "country": spec["country"],
        "length": round(float(L), 1), "baseAltitude": round(base, 1), "clockwise": spec["direction"] > 0,
        "source": "openstreetmap+srtm30m", "treeDensity": spec["trees"], "stride": 6,
        "points": data, "corners": corners,
    }
    path = os.path.join(gt.OUT_DIR, f"{args.track}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"))
    print(f"Готово: {path}  перепад высот {h.max() - h.min():.0f} м")

    if args.plot:
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(1, 2, figsize=(14, 6))
        sc = ax[0].scatter(x, z, c=h, s=2, cmap="terrain")
        ax[0].plot(x[0], z[0], "r^")
        ax[0].set_aspect("equal")
        fig.colorbar(sc, ax=ax[0])
        ax[1].plot(s_out, h)
        plt.show()


if __name__ == "__main__":
    main()
