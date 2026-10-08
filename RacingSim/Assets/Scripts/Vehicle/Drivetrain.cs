using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>
    /// Двигатель (кривая момента, инерция, торможение двигателем, ограничитель),
    /// автоматическое сцепление, секвентальная КПП и самоблокирующийся дифференциал
    /// (преднатяг + рампы под газом/сбросом). Привод — задний.
    /// </summary>
    public class Drivetrain
    {
        readonly EngineData eng;
        readonly GearboxData box;
        readonly DiffData diff;

        public int Gear { get; private set; } = 1;       // -1 = R, 0 = N, 1..n
        public int GearCount => box.ratios.Length;
        public float EngineOmega { get; private set; }
        public float EngineRpm => EngineOmega * 60f / (2f * Mathf.PI);
        public float EngineTorque { get; private set; }
        public float ThrottleActual { get; private set; }
        public bool ClutchLocked { get; private set; }
        public float ClutchEngagement { get; private set; }
        public bool IsShifting => shiftTimer > 0f;
        public bool LimiterActive { get; private set; }
        public float LimiterRpm => eng.limiterRpm;
        public float IdleRpm => eng.idleRpm;

        float shiftTimer;
        int pendingGear;
        float limiterCut;

        static float RpmToOmega(float rpm) => rpm * 2f * Mathf.PI / 60f;

        public Drivetrain(EngineData engine, GearboxData gearbox, DiffData differential)
        {
            eng = engine;
            box = gearbox;
            diff = differential;
            EngineOmega = RpmToOmega(eng.idleRpm);
        }

        public float GearRatio(int gear)
        {
            if (gear == 0) return 0f;
            if (gear < 0) return -box.reverseRatio * box.finalDrive;
            return box.ratios[Mathf.Clamp(gear - 1, 0, box.ratios.Length - 1)] * box.finalDrive;
        }

        public float TotalRatio => GearRatio(Gear);

        public bool RequestGear(int gear)
        {
            gear = Mathf.Clamp(gear, -1, GearCount);
            if (gear == Gear || IsShifting) return false;
            pendingGear = gear;
            shiftTimer = box.shiftTime;
            return true;
        }

        public void ShiftUp() => RequestGear(Gear + 1);
        public void ShiftDown() => RequestGear(Gear - 1);

        /// <summary>Максимальный момент по кривой (полный газ) при данных оборотах.</summary>
        public float CurveTorque(float rpm)
        {
            var r = eng.torqueRpm;
            var t = eng.torqueNm;
            if (rpm <= r[0]) return t[0] * Mathf.Clamp01(rpm / r[0]);
            for (int i = 1; i < r.Length; i++)
            {
                if (rpm <= r[i])
                    return Mathf.Lerp(t[i - 1], t[i], (rpm - r[i - 1]) / (r[i] - r[i - 1]));
            }
            return t[t.Length - 1];
        }

        float NetEngineTorque(float omega, float throttle)
        {
            float rpm = omega * 60f / (2f * Mathf.PI);
            float friction = eng.frictionTorque + eng.frictionPerRpm * rpm;
            return throttle * (CurveTorque(rpm) + friction) - friction;
        }

        /// <summary>Шаг трансмиссии. Задаёт driveTorque и приведённую инерцию ведущим колёсам.</summary>
        public void Step(float throttleInput, Wheel left, Wheel right, float dt)
        {
            // ---- КПП ----
            if (shiftTimer > 0f)
            {
                shiftTimer -= dt;
                if (shiftTimer <= box.shiftTime * 0.5f && Gear != pendingGear)
                    Gear = pendingGear;
            }

            float idleOmega = RpmToOmega(eng.idleRpm);
            float limiterOmega = RpmToOmega(eng.limiterRpm);

            // ---- дроссель: холостой ход, ограничитель оборотов, инерция турбин ----
            float throttleTarget = throttleInput;
            if (EngineOmega < idleOmega)
                throttleTarget = Mathf.Max(throttleTarget, Mathf.Clamp01((idleOmega - EngineOmega) / 30f) * 0.35f);
            if (EngineOmega > limiterOmega) limiterCut = 0.03f;
            if (limiterCut > 0f)
            {
                limiterCut -= dt;
                throttleTarget = 0f;
            }
            LimiterActive = limiterCut > 0f;
            if (IsShifting && Gear > 0) throttleTarget *= 0.2f; // отсечка зажигания при переключении
            ThrottleActual = Mathf.MoveTowards(ThrottleActual, throttleTarget, eng.throttleResponse * dt);

            float ratio = TotalRatio;
            float wheelOmega = 0.5f * (left.angularVelocity + right.angularVelocity);
            float driveOmega = wheelOmega * ratio; // обороты трансмиссии на стороне двигателя

            // ---- автоматическое сцепление ----
            if (Gear == 0 || IsShifting)
            {
                ClutchEngagement = 0f;
                ClutchLocked = false;
            }
            else if (Mathf.Abs(driveOmega) > idleOmega * 1.05f)
            {
                ClutchEngagement = 1f;
            }
            else
            {
                // старт с места: «центробежное» сцепление — смыкается с ростом оборотов
                float launchOmega = idleOmega + RpmToOmega(2500f);
                ClutchEngagement = Mathf.Clamp01((EngineOmega - idleOmega * 1.1f) / (launchOmega - idleOmega * 1.1f));
                if (Mathf.Abs(driveOmega) < idleOmega * 0.95f) ClutchLocked = false;
            }

            float engineInertia = eng.inertia;
            float torqueAtWheels;
            float extraInertia = 0f;

            if (ClutchLocked && ClutchEngagement >= 1f)
            {
                EngineOmega = Mathf.Max(driveOmega, 0f);
                EngineTorque = NetEngineTorque(EngineOmega, ThrottleActual);
                float eff = EngineTorque >= 0f ? box.efficiency : 1f / box.efficiency;
                torqueAtWheels = EngineTorque * ratio * eff;
                extraInertia = engineInertia * ratio * ratio * 0.5f;
                if (driveOmega < 0f) ClutchLocked = false; // откат назад на передней передаче
            }
            else
            {
                EngineTorque = NetEngineTorque(EngineOmega, ThrottleActual);
                float cap = box.clutchMaxTorque * ClutchEngagement;
                float slip = EngineOmega - driveOmega;
                float gain = engineInertia / dt * 0.5f;
                float clutchTorque = Mathf.Clamp(slip * gain, -cap, cap);
                float prevSlip = slip;
                EngineOmega += (EngineTorque - clutchTorque) / engineInertia * dt;
                EngineOmega = Mathf.Max(EngineOmega, 0f);
                torqueAtWheels = clutchTorque * ratio * box.efficiency;
                float newSlip = EngineOmega - driveOmega;
                if (ClutchEngagement >= 1f && (Mathf.Sign(newSlip) != Mathf.Sign(prevSlip) || Mathf.Abs(newSlip) < 3f))
                    ClutchLocked = true; // синхронизация оборотов после переключения
            }

            // ---- самоблокирующийся дифференциал ----
            float rampCoef = torqueAtWheels >= 0f ? diff.powerRamp : diff.coastRamp;
            float lockCap = diff.preload + rampCoef * Mathf.Abs(torqueAtWheels);
            float wheelInertia = left.tyre.inertia + extraInertia;
            float lockGain = wheelInertia / dt * 0.5f;
            float transfer = Mathf.Clamp((left.angularVelocity - right.angularVelocity) * lockGain, -lockCap, lockCap);

            left.driveTorque = torqueAtWheels * 0.5f - transfer;
            right.driveTorque = torqueAtWheels * 0.5f + transfer;
            left.extraInertia = right.extraInertia = extraInertia;
        }

        public void Reset()
        {
            Gear = 1;
            shiftTimer = 0f;
            EngineOmega = RpmToOmega(eng.idleRpm);
            ClutchLocked = false;
            ThrottleActual = 0f;
        }
    }
}
