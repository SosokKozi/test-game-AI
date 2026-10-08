using System.Collections.Generic;
using RacingSim.Core;
using RacingSim.Vehicle;
using UnityEngine;
using UnityEngine.Rendering;

namespace RacingSim.Track
{
    /// <summary>
    /// Строит трассу в рантайме по центральной линии: асфальт с виражами, разметка,
    /// поребрики (kerbs) в поворотах, зоны безопасности (трава/гравий), отбойники,
    /// стартовая линия с порталом, рельеф местности (Terrain) и лес.
    /// </summary>
    public static class TrackBuilder
    {
        const int ChunkSize = 120;
        const float KerbWidth = 1.3f;
        const float WallHeight = 1.1f;

        public static GameObject Build(TrackData t)
        {
            var root = new GameObject("Track_" + t.id);
            int n = t.Count;

            // сглаженная кривизна → где нужны поребрики и гравий
            var curv = Smooth(t.curvature, 6);
            var kerb = new bool[n];
            for (int i = 0; i < n; i++)
                if (Mathf.Abs(curv[i]) > 1f / 260f)
                    for (int k = -8; k <= 8; k++) kerb[t.Wrap(i + k)] = true;

            for (int start = 0; start < n; start += ChunkSize)
            {
                int end = Mathf.Min(start + ChunkSize, n); // точки [start, end], последняя — следующая по кругу
                var road = new MeshBuilder();
                var lines = new MeshBuilder();
                var kerbR = new MeshBuilder();
                var kerbW = new MeshBuilder();
                var grass = new MeshBuilder();
                var gravel = new MeshBuilder();
                var walls = new MeshBuilder();

                for (int i = start; i < end; i++)
                {
                    int j = t.Wrap(i + 1);
                    float d0 = t.distance[i];
                    float d1 = j == 0 ? t.length : t.distance[j];

                    Edge(t, i, out var l0, out var r0);
                    Edge(t, j, out var l1, out var r1);
                    Vector3 up0 = t.up[i], up1 = t.up[j];
                    Vector3 nUp = (up0 + up1).normalized;

                    // асфальт: U поперёк, V вдоль (повтор текстуры каждые 8 м)
                    road.Quad(l0, r0, r1, l1, nUp,
                        new Vector2(0, d0 / 8f), new Vector2(t.width[i] / 8f, d0 / 8f),
                        new Vector2(t.width[j] / 8f, d1 / 8f), new Vector2(0, d1 / 8f));

                    // белые линии по краям
                    Vector3 lift = nUp * 0.01f;
                    Vector3 ri = t.right[i], rj = t.right[j];
                    lines.Quad(l0 + ri * 0.25f + lift, l0 + ri * 0.4f + lift, l1 + rj * 0.4f + lift, l1 + rj * 0.25f + lift, nUp);
                    lines.Quad(r0 - ri * 0.4f + lift, r0 - ri * 0.25f + lift, r1 - rj * 0.25f + lift, r1 - rj * 0.4f + lift, nUp);

                    // поребрики (красно-белые, чередование каждые ~4 м)
                    if (kerb[i])
                    {
                        var kb = ((i / 2) % 2 == 0) ? kerbR : kerbW;
                        KerbStrip(kb, l0, l1, -ri, -rj, nUp);
                        KerbStrip(kb, r0, r1, ri, rj, nUp);
                    }

                    // зоны безопасности: горизонтально наружу от кромки, лёгкий уклон вниз
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 e0 = side < 0 ? l0 : r0, e1 = side < 0 ? l1 : r1;
                        Vector3 out0 = Flat(t.right[i]) * side, out1 = Flat(t.right[j]) * side;
                        float w0 = t.runoff[i], w1 = t.runoff[j];
                        Vector3 a = e0 + Vector3.down * 0.02f, b = e1 + Vector3.down * 0.02f;
                        Vector3 c = e1 + out1 * w1 + Vector3.down * 0.15f, d = e0 + out0 * w0 + Vector3.down * 0.15f;
                        // гравий — снаружи медленных поворотов
                        bool outside = (curv[i] > 1f / 160f && side < 0) || (curv[i] < -1f / 160f && side > 0);
                        var mb = outside && w0 > 6f ? gravel : grass;
                        mb.Quad(a, b, c, d, Vector3.up,
                            new Vector2(0, d0 / 6f), new Vector2(0, d1 / 6f), new Vector2(w1 / 6f, d1 / 6f), new Vector2(w0 / 6f, d0 / 6f));

                        // отбойник: внутренняя грань + верх
                        Vector3 bottom0 = d + Vector3.down * 0.4f, bottom1 = c + Vector3.down * 0.4f;
                        Vector3 top0 = d + Vector3.up * WallHeight, top1 = c + Vector3.up * WallHeight;
                        walls.Quad(bottom0, bottom1, top1, top0, -out0);
                        walls.Quad(top0, top1, top1 + out1 * 0.3f, top0 + out0 * 0.3f, Vector3.up);
                    }
                }

                var chunk = new GameObject($"Chunk_{start / ChunkSize:00}").transform;
                chunk.SetParent(root.transform, false);
                road.Build("Road", chunk, TrackMaterials.Asphalt, true, SurfaceType.Asphalt);
                lines.Build("Lines", chunk, TrackMaterials.Line, false);
                if (!kerbR.IsEmpty) kerbR.Build("KerbRed", chunk, TrackMaterials.KerbRed, true, SurfaceType.Kerb);
                if (!kerbW.IsEmpty) kerbW.Build("KerbWhite", chunk, TrackMaterials.KerbWhite, true, SurfaceType.Kerb);
                if (!grass.IsEmpty) grass.Build("Grass", chunk, TrackMaterials.Grass, true, SurfaceType.Grass);
                if (!gravel.IsEmpty) gravel.Build("Gravel", chunk, TrackMaterials.Gravel, true, SurfaceType.Gravel);
                walls.Build("Walls", chunk, TrackMaterials.Wall, true, SurfaceType.Concrete);
            }

            BuildStartLine(t, root.transform);
            var terrain = BuildTerrain(t, root.transform);
            BuildTrees(t, root.transform, terrain);
            BuildCornerSigns(t, root.transform);
            return root;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
        }

        static void Edge(TrackData t, int i, out Vector3 left, out Vector3 right)
        {
            Vector3 half = t.right[i] * (t.width[i] * 0.5f);
            left = t.center[i] - half;
            right = t.center[i] + half;
        }

        /// <summary>Поребрик: от кромки наружу, приподнят посередине.</summary>
        static void KerbStrip(MeshBuilder mb, Vector3 e0, Vector3 e1, Vector3 out0, Vector3 out1, Vector3 up)
        {
            Vector3 lift = up * 0.008f;
            Vector3 m0 = e0 + out0 * (KerbWidth * 0.5f) + up * 0.045f, m1 = e1 + out1 * (KerbWidth * 0.5f) + up * 0.045f;
            Vector3 o0 = e0 + out0 * KerbWidth + up * 0.01f, o1 = e1 + out1 * KerbWidth + up * 0.01f;
            Vector3 a0 = e0 - out0 * 0.05f + lift, a1 = e1 - out1 * 0.05f + lift;
            mb.Quad(a0, m0, m1, a1, up);
            mb.Quad(m0, o0, o1, m1, up);
        }

        static float[] Smooth(float[] src, int radius)
        {
            int n = src.Length;
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                for (int k = -radius; k <= radius; k++) s += src[((i + k) % n + n) % n];
                dst[i] = s / (2 * radius + 1);
            }
            return dst;
        }

        static void BuildStartLine(TrackData t, Transform parent)
        {
            Vector3 c = t.center[0], f = t.tangent[0], r = t.right[0], u = t.up[0];
            float w = t.width[0];
            var mb = new MeshBuilder();
            // шахматная полоса
            int cells = Mathf.RoundToInt(w / 0.5f);
            for (int k = 0; k < cells; k++)
            for (int row = 0; row < 2; row++)
            {
                if ((k + row) % 2 == 1) continue;
                Vector3 p = c - r * (w * 0.5f) + r * (k * w / cells) + f * (row * 0.5f - 0.5f) + u * 0.012f;
                mb.Quad(p, p + r * (w / cells), p + r * (w / cells) + f * 0.5f, p + f * 0.5f, u);
            }
            mb.Build("StartLine", parent, TrackMaterials.Line, false);

            // портал над стартом
            float span = w * 0.5f + 2.5f;
            var mat = MaterialFactory.Create("gantry", new Color(0.15f, 0.15f, 0.18f), 0.4f, 0.6f);
            var post = MaterialFactory.Create("gantryPost", new Color(0.8f, 0.8f, 0.82f), 0.4f, 0.6f);
            Box("GantryL", parent, c - r * span + Vector3.up * 3.5f, Quaternion.LookRotation(f), new Vector3(0.5f, 7f, 0.5f), post);
            Box("GantryR", parent, c + r * span + Vector3.up * 3.5f, Quaternion.LookRotation(f), new Vector3(0.5f, 7f, 0.5f), post);
            Box("GantryBeam", parent, c + Vector3.up * 6.6f, Quaternion.LookRotation(f), new Vector3(span * 2f, 1.2f, 0.6f), mat);
        }

        static void Box(string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ рельеф

        /// <summary>
        /// Рельеф: у трассы — чуть ниже полотна, вдали — плавная интерполяция высот
        /// (обратные расстояния), поэтому холмы Спа и гора Батерста видны и вокруг трассы.
        /// </summary>
        static Terrain BuildTerrain(TrackData t, Transform parent)
        {
            const int res = 513;
            const float margin = 450f;
            Vector3 min = t.center[0], max = t.center[0];
            foreach (var p in t.center) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            min -= new Vector3(margin, 0, margin);
            max += new Vector3(margin, 0, margin);
            float sizeX = max.x - min.x, sizeZ = max.z - min.z;
            float baseY = min.y - 40f;
            float heightRange = (max.y - min.y) + 120f;

            // пространственный хеш точек центральной линии
            const float cell = 30f;
            var grid = new Dictionary<long, List<int>>();
            long Key(int gx, int gz) => ((long)gx << 32) ^ (uint)gz;
            for (int i = 0; i < t.Count; i++)
            {
                int gx = Mathf.FloorToInt((t.center[i].x - min.x) / cell), gz = Mathf.FloorToInt((t.center[i].z - min.z) / cell);
                long k = Key(gx, gz);
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>();
                list.Add(i);
            }

            // грубое поле высот (IDW) по прореженным точкам
            const int coarse = 48;
            var far = new float[coarse + 1, coarse + 1];
            for (int z = 0; z <= coarse; z++)
            for (int x = 0; x <= coarse; x++)
            {
                float px = min.x + sizeX * x / coarse, pz = min.z + sizeZ * z / coarse;
                double ws = 0, hs = 0;
                for (int i = 0; i < t.Count; i += 8)
                {
                    float dx = t.center[i].x - px, dz = t.center[i].z - pz;
                    double w = 1.0 / System.Math.Pow(dx * dx + dz * dz + 400.0, 1.5);
                    ws += w;
                    hs += w * t.center[i].y;
                }
                far[x, z] = (float)(hs / ws);
            }

            var heights = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / (res - 1), v = (float)z / (res - 1);
                float px = min.x + sizeX * u, pz = min.z + sizeZ * v;

                // ближайшая точка трассы (поиск в соседних ячейках)
                int gx = Mathf.FloorToInt((px - min.x) / cell), gz = Mathf.FloorToInt((pz - min.z) / cell);
                float best = float.MaxValue;
                int bi = -1;
                for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (!grid.TryGetValue(Key(gx + dx, gz + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        float ddx = t.center[i].x - px, ddz = t.center[i].z - pz;
                        float d2 = ddx * ddx + ddz * ddz;
                        if (d2 < best) { best = d2; bi = i; }
                    }
                }

                float fx = u * coarse, fz = v * coarse;
                int ix = Mathf.Min((int)fx, coarse - 1), iz = Mathf.Min((int)fz, coarse - 1);
                float tx = fx - ix, tz = fz - iz;
                float hFar = Mathf.Lerp(Mathf.Lerp(far[ix, iz], far[ix + 1, iz], tx), Mathf.Lerp(far[ix, iz + 1], far[ix + 1, iz + 1], tx), tz);

                float h = hFar;
                if (bi >= 0)
                {
                    float dist = Mathf.Sqrt(best);
                    float edge = t.width[bi] * 0.5f + t.runoff[bi];
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge + 2f, edge + 60f, dist));
                    float bankDrop = Mathf.Abs(Mathf.Sin(t.bank[bi] * Mathf.Deg2Rad)) * t.width[bi] * 0.5f;
                    float near = t.center[bi].y - 0.7f - bankDrop;
                    h = Mathf.Lerp(near, hFar, blend);
                }
                // лёгкая «холмистость» вдали от трассы
                h += (Mathf.PerlinNoise(px * 0.004f, pz * 0.004f) - 0.5f) * 14f * Mathf.InverseLerp(80f, 400f, Mathf.Sqrt(best));
                heights[z, x] = Mathf.Clamp01((h - baseY) / heightRange);
            }

            var data = new TerrainData { heightmapResolution = res };
            data.size = new Vector3(sizeX, heightRange, sizeZ);
            data.SetHeights(0, 0, heights);
            var layer = new TerrainLayer { diffuseTexture = TrackMaterials.GrassTexture, tileSize = new Vector2(12f, 12f) };
            data.terrainLayers = new[] { layer };

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(min.x, baseY, min.z);
            var terrain = go.GetComponent<Terrain>();
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (sh != null) terrain.materialTemplate = new Material(sh);
            }
            terrain.heightmapPixelError = 4f;
            SurfaceInfo.Configure(go, SurfaceType.Grass);
            return terrain;
        }

        // ------------------------------------------------------------------ лес

        static void BuildTrees(TrackData t, Transform parent, Terrain terrain)
        {
            int count = Mathf.RoundToInt(t.length / 3f * t.treeDensity);
            if (count <= 0) return;
            var rnd = new System.Random(t.id.GetHashCode());
            var crowns = new MeshBuilder();
            var trunks = new MeshBuilder();
            var holder = new GameObject("Trees").transform;
            holder.SetParent(parent, false);
            int part = 0;

            for (int k = 0; k < count; k++)
            {
                int i = rnd.Next(t.Count);
                int side = rnd.Next(2) == 0 ? -1 : 1;
                float clearance = t.width[i] * 0.5f + t.runoff[i] + 10f;
                float dist = clearance + (float)(rnd.NextDouble() * rnd.NextDouble()) * 220f;
                Vector3 p = t.center[i] + Flat(t.right[i]) * side * dist;
                // не сажаем деревья на другие участки трассы
                int near = t.NearestIndex(p, i, 400);
                if ((t.center[near] - p).sqrMagnitude < Sq(t.width[near] * 0.5f + t.runoff[near] + 8f)) continue;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                float hgt = 9f + (float)rnd.NextDouble() * 12f;
                float rad = hgt * (0.22f + (float)rnd.NextDouble() * 0.1f);
                AddTree(crowns, trunks, p, hgt, rad);

                if (crowns.VertexCount > 60000)
                {
                    crowns.Build("Crowns" + part, holder, TrackMaterials.Tree, false);
                    trunks.Build("Trunks" + part, holder, TrackMaterials.Trunk, false);
                    crowns = new MeshBuilder();
                    trunks = new MeshBuilder();
                    part++;
                }
            }
            if (!crowns.IsEmpty)
            {
                crowns.Build("Crowns" + part, holder, TrackMaterials.Tree, false);
                trunks.Build("Trunks" + part, holder, TrackMaterials.Trunk, false);
            }
        }

        static float Sq(float x) => x * x;

        static void AddTree(MeshBuilder crowns, MeshBuilder trunks, Vector3 p, float h, float r)
        {
            const int sides = 7;
            float trunkH = h * 0.25f;
            Vector3 tip = p + Vector3.up * h;
            Vector3 baseC = p + Vector3.up * trunkH;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 n0 = (d0 * h + Vector3.up * r).normalized, n1 = (d1 * h + Vector3.up * r).normalized;
                crowns.Triangle(baseC + d0 * r, baseC + d1 * r, tip, n0, n1, (n0 + n1).normalized);
                crowns.Triangle(baseC + d1 * r, baseC + d0 * r, baseC, Vector3.down, Vector3.down, Vector3.down);
                float tr = r * 0.12f;
                trunks.Quad(p + d0 * tr, p + d1 * tr, baseC + d1 * tr, baseC + d0 * tr, (d0 + d1).normalized);
            }
        }

        // ------------------------------------------------------------------ таблички поворотов

        static void BuildCornerSigns(TrackData t, Transform parent)
        {
            if (t.corners == null) return;
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            if (font == null) return;
            var boardMat = MaterialFactory.Create("signBoard", new Color(0.1f, 0.12f, 0.35f), 0.3f);
            foreach (var c in t.corners)
            {
                t.Sample(c.distance - 60f, out var pos, out var fwd, out _);
                int i = t.NearestIndex(pos);
                // ставим табличку снаружи поворота
                int side = t.curvature[t.Wrap(i + 30)] > 0f ? -1 : 1;
                Vector3 p = pos + Flat(t.right[i]) * side * (t.width[i] * 0.5f + Mathf.Min(t.runoff[i], 8f) - 1f) + Vector3.up * 2.2f;
                var rot = Quaternion.LookRotation(-Flat(fwd));
                Box("Sign_" + c.name, parent, p + Vector3.up * 0.2f, rot, new Vector3(Mathf.Max(3f, c.name.Length * 0.32f), 1.1f, 0.1f), boardMat);
                var go = new GameObject("SignText_" + c.name);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(p + Vector3.up * 0.2f - Flat(fwd) * 0.07f, Quaternion.LookRotation(Flat(fwd)));
                var tm = go.AddComponent<TextMesh>();
                tm.text = c.name;
                tm.font = font;
                tm.fontSize = 48;
                tm.characterSize = 0.06f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Color.white;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }
    }
}
