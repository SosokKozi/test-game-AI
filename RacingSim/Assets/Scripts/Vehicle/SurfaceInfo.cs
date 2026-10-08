using System.Collections.Generic;
using UnityEngine;

namespace RacingSim.Vehicle
{
    public enum SurfaceType { Asphalt, Kerb, Grass, Gravel, Concrete }

    /// <summary>Свойства покрытия, на которое опирается колесо. Вешается на объект с коллайдером.</summary>
    public class SurfaceInfo : MonoBehaviour
    {
        public SurfaceType type = SurfaceType.Asphalt;
        public float grip = 1f;              // множитель μ
        public float rollingDrag = 0f;       // доп. сопротивление качению (доля нагрузки)
        public float bumpiness = 0f;         // амплитуда шума высоты, м

        static readonly Dictionary<int, SurfaceInfo> Cache = new Dictionary<int, SurfaceInfo>();
        static SurfaceInfo defaultSurface;

        public static SurfaceInfo Default
        {
            get
            {
                if (defaultSurface == null)
                {
                    var go = new GameObject("DefaultSurface") { hideFlags = HideFlags.HideAndDontSave };
                    defaultSurface = go.AddComponent<SurfaceInfo>();
                }
                return defaultSurface;
            }
        }

        public static SurfaceInfo Get(Collider c)
        {
            if (c == null) return Default;
            int id = c.GetInstanceID();
            if (!Cache.TryGetValue(id, out var s) || s == null)
            {
                s = c.GetComponent<SurfaceInfo>();
                if (s == null) s = Default;
                Cache[id] = s;
            }
            return s;
        }

        public static void Configure(GameObject go, SurfaceType type)
        {
            var s = go.GetComponent<SurfaceInfo>();
            if (s == null) s = go.AddComponent<SurfaceInfo>();
            s.type = type;
            switch (type)
            {
                case SurfaceType.Asphalt: s.grip = 1f; s.rollingDrag = 0f; s.bumpiness = 0f; break;
                case SurfaceType.Kerb: s.grip = 0.9f; s.rollingDrag = 0.005f; s.bumpiness = 0.008f; break;
                case SurfaceType.Concrete: s.grip = 0.85f; s.rollingDrag = 0.002f; s.bumpiness = 0f; break;
                case SurfaceType.Grass: s.grip = 0.45f; s.rollingDrag = 0.06f; s.bumpiness = 0.015f; break;
                case SurfaceType.Gravel: s.grip = 0.5f; s.rollingDrag = 0.25f; s.bumpiness = 0.02f; break;
            }
        }
    }
}
