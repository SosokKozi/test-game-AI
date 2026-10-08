using System;
using UnityEngine;

namespace RacingSim.Track
{
    [Serializable]
    public class TrackCorner
    {
        public string name;
        public float distance;
    }

    /// <summary>
    /// Центральная линия трассы из Resources/Tracks/*.json (генерируется tools/trackgen).
    /// points — плоский массив: x, y(высота), z, ширина, вираж(°), зона безопасности(м) × N.
    /// </summary>
    [Serializable]
    public class TrackData
    {
        public string id;
        public string displayName;
        public string country;
        public float length;
        public float baseAltitude;
        public bool clockwise;
        public string source;
        public float treeDensity = 0.5f;
        public int stride = 6;
        public float[] points;
        public TrackCorner[] corners;

        [NonSerialized] public Vector3[] center;
        [NonSerialized] public float[] width;
        [NonSerialized] public float[] bank;
        [NonSerialized] public float[] runoff;
        [NonSerialized] public float[] distance;   // накопленная дистанция до точки i
        [NonSerialized] public Vector3[] tangent;
        [NonSerialized] public Vector3[] right;     // с учётом виража
        [NonSerialized] public Vector3[] up;
        [NonSerialized] public float[] curvature;   // 1/м, знак: + направо

        public int Count => center.Length;

        public static TrackData Load(string id)
        {
            var asset = Resources.Load<TextAsset>("Tracks/" + id);
            if (asset == null) throw new ArgumentException("Track not found: " + id);
            var data = JsonUtility.FromJson<TrackData>(asset.text);
            data.Prepare();
            return data;
        }

        public void Prepare()
        {
            int n = points.Length / stride;
            center = new Vector3[n];
            width = new float[n];
            bank = new float[n];
            runoff = new float[n];
            for (int i = 0; i < n; i++)
            {
                int o = i * stride;
                center[i] = new Vector3(points[o], points[o + 1], points[o + 2]);
                width[i] = points[o + 3];
                bank[i] = stride > 4 ? points[o + 4] : 0f;
                runoff[i] = stride > 5 ? points[o + 5] : 12f;
            }

            distance = new float[n];
            for (int i = 1; i < n; i++) distance[i] = distance[i - 1] + Vector3.Distance(center[i - 1], center[i]);
            length = distance[n - 1] + Vector3.Distance(center[n - 1], center[0]);

            tangent = new Vector3[n];
            right = new Vector3[n];
            up = new Vector3[n];
            curvature = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = center[(i - 1 + n) % n], next = center[(i + 1) % n];
                Vector3 t = (next - prev).normalized;
                tangent[i] = t;
                // горизонтальная «правая» ось, затем поворот на угол виража вокруг касательной
                Vector3 flatRight = Vector3.Cross(Vector3.up, t).normalized;
                Vector3 r = Quaternion.AngleAxis(-bank[i], t) * flatRight;
                right[i] = r;
                up[i] = Vector3.Cross(t, r).normalized;

                Vector3 a = new Vector3(prev.x, 0, prev.z), b = new Vector3(center[i].x, 0, center[i].z), c = new Vector3(next.x, 0, next.z);
                Vector3 d1 = (b - a).normalized, d2 = (c - b).normalized;
                float ang = Vector3.SignedAngle(d1, d2, Vector3.up) * Mathf.Deg2Rad;
                float ds = 0.5f * ((b - a).magnitude + (c - b).magnitude);
                curvature[i] = ds > 1e-3f ? ang / ds : 0f;
            }
        }

        public int Wrap(int i) => ((i % Count) + Count) % Count;

        /// <summary>Индекс ближайшей точки; если задан hint — ищем локально (быстро).</summary>
        public int NearestIndex(Vector3 pos, int hint = -1, int window = 60)
        {
            int best = 0;
            float bestD = float.MaxValue;
            if (hint >= 0)
            {
                for (int k = -window; k <= window; k++)
                {
                    int i = Wrap(hint + k);
                    float d = (center[i] - pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (bestD < 40f * 40f) return best;
            }
            for (int i = 0; i < Count; i++)
            {
                float d = (center[i] - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Точка на расстоянии s от старта (с интерполяцией).</summary>
        public void Sample(float s, out Vector3 pos, out Vector3 fwd, out Vector3 upDir)
        {
            s = Mathf.Repeat(s, length);
            int lo = 0, hi = Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (distance[mid] <= s) lo = mid; else hi = mid - 1;
            }
            int j = Wrap(lo + 1);
            float segLen = (j == 0 ? length : distance[j]) - distance[lo];
            float t = segLen > 1e-4f ? (s - distance[lo]) / segLen : 0f;
            pos = Vector3.Lerp(center[lo], center[j], t);
            fwd = Vector3.Slerp(tangent[lo], tangent[j], t).normalized;
            upDir = Vector3.Slerp(up[lo], up[j], t).normalized;
        }
    }
}
