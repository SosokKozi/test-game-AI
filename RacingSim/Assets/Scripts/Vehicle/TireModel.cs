using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>
    /// Модель шины на основе «Magic Formula» Пасейки с нормализованным комбинированным
    /// скольжением (friction ellipse). Возвращает продольную и поперечную силы в пятне
    /// контакта по продольному скольжению κ, tan(угла увода) и вертикальной нагрузке.
    /// </summary>
    public class TireModel
    {
        readonly TyreData d;
        readonly float shapeB;
        readonly float tanPeakAngle;

        public TireModel(TyreData data)
        {
            d = data;
            tanPeakAngle = Mathf.Tan(d.peakSlipAngleDeg * Mathf.Deg2Rad);
            shapeB = SolveB(d.shapeC, d.falloffE);
        }

        /// <summary>Подбираем B так, чтобы пик кривой был ровно при нормализованном скольжении s = 1.</summary>
        static float SolveB(float c, float e)
        {
            // пик: C·atan(B − E(B − atan B)) = π/2
            float target = Mathf.Tan(Mathf.PI / (2f * c));
            float lo = 0.01f, hi = 50f;
            for (int i = 0; i < 60; i++)
            {
                float b = 0.5f * (lo + hi);
                float x = b - e * (b - Mathf.Atan(b));
                if (x < target) lo = b; else hi = b;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>Нормализованная кривая: 0 при s=0, 1 в пике (s=1), затем спад.</summary>
        public float Curve(float s)
        {
            float bs = shapeB * s;
            return Mathf.Sin(d.shapeC * Mathf.Atan(bs - d.falloffE * (bs - Mathf.Atan(bs))));
        }

        /// <summary>Коэффициент сцепления с учётом чувствительности к нагрузке.</summary>
        public float LoadFactor(float fz)
        {
            float k = 1f - d.loadSensitivity * (fz / d.nominalLoad - 1f);
            return Mathf.Clamp(k, 0.6f, 1.25f);
        }

        /// <param name="slipRatio">κ, &gt;0 — пробуксовка (колесо быстрее дороги)</param>
        /// <param name="tanSlipAngle">tan α, знак: положительный — сила влево от направления качения отрицательна</param>
        /// <param name="fz">вертикальная нагрузка, Н</param>
        /// <param name="surfaceGrip">множитель сцепления покрытия</param>
        /// <param name="fx">продольная сила, Н (вперёд по колесу)</param>
        /// <param name="fy">поперечная сила, Н (вдоль оси «вправо» колеса)</param>
        public void ComputeForces(float slipRatio, float tanSlipAngle, float fz, float surfaceGrip,
            out float fx, out float fy, out float combinedSlip)
        {
            fx = fy = 0f;
            combinedSlip = 0f;
            if (fz <= 0f) return;

            float sx = slipRatio / d.peakSlipRatio;
            float sy = tanSlipAngle / tanPeakAngle;
            float s = Mathf.Sqrt(sx * sx + sy * sy);
            combinedSlip = s;
            if (s < 1e-6f) return;

            float mu = LoadFactor(fz) * surfaceGrip;
            float f = Curve(s) / s;
            fx = d.muLongitudinal * mu * fz * f * sx;
            fy = d.muLateral * mu * fz * f * sy;
        }
    }
}
