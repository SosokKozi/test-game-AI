using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>Настройки помощников водителя (меняются в меню).</summary>
    public static class DrivingAids
    {
        public static bool AutoGearbox = true;
        public static int AbsLevel = 2;   // 0 = выкл, 1..3
        public static int TcLevel = 2;    // 0 = выкл, 1..3
    }

    /// <summary>
    /// Автомобиль: Rigidbody + 4 колеса + трансмиссия + аэродинамика + ABS/TC.
    /// Порядок шага физики: подвеска → стабилизаторы → трансмиссия → шины/вращение колёс → аэро.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        public CarSpec Spec { get; private set; }
        public Wheel[] Wheels { get; private set; }  // FL, FR, RL, RR
        public Drivetrain Drivetrain { get; private set; }
        public DriverInput Input { get; } = new DriverInput();
        public Rigidbody Body { get; private set; }

        public float SpeedMs { get; private set; }
        public float ForwardSpeedMs { get; private set; }
        public float SpeedKmh => SpeedMs * 3.6f;
        public bool AbsActive { get; private set; }
        public bool TcActive { get; private set; }
        public float Downforce { get; private set; }
        public float Drag { get; private set; }
        public Vector3 LocalAcceleration { get; private set; }   // м/с² в осях машины (для HUD: g-силы)

        /// <summary>Событие «вернуть на трассу» (R) — обрабатывает RaceSession.</summary>
        public System.Action<VehicleController> ResetRequested;

        CarVisual visual;
        int groundMask;
        float[] absFactor = { 1f, 1f, 1f, 1f };
        float reverseTimer;
        float shiftCooldown;
        Vector3 lastVelocity;

        public void Init(CarSpec spec)
        {
            Spec = spec;
            Body = GetComponent<Rigidbody>();
            gameObject.layer = 2; // Ignore Raycast — лучи подвески не цепляют собственный кузов
            groundMask = ~(1 << 2);

            var d = spec.dimensions;
            var m = spec.mass;

            // кузов и коллайдер — до задания массы, чтобы Unity не пересчитал центр масс по коллайдерам
            visual = CarBodyBuilder.Build(spec, transform);
            var box = gameObject.AddComponent<BoxCollider>();
            float noseZ = d.wheelbase * 0.5f + d.frontOverhang;
            float tailZ = -d.wheelbase * 0.5f - (d.length - d.wheelbase - d.frontOverhang);
            box.center = new Vector3(0f, 0.18f + (d.height - 0.18f) * 0.5f, (noseZ + tailZ) * 0.5f);
            box.size = new Vector3(d.width * 0.95f, d.height - 0.18f, d.length);

            Body.mass = m.mass;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.maxAngularVelocity = 60f;

            // начало координат машины — на земле посередине между осями
            float frontZ = d.wheelbase * 0.5f;
            float rearZ = -d.wheelbase * 0.5f;
            Body.centerOfMass = new Vector3(0f, m.cgHeight, rearZ + d.wheelbase * m.weightDistributionFront);

            // тензор инерции — «коробка» с поправкой на концентрацию массы
            float l = d.length, w = d.width, h = d.height * 0.8f;
            float s = m.inertiaScale * m.mass / 12f;
            Body.inertiaTensor = new Vector3(s * (h * h + l * l), s * (w * w + l * l), s * (w * w + h * h));
            Body.inertiaTensorRotation = Quaternion.identity;

            // колёса
            var sf = spec.suspensionFront;
            var sr = spec.suspensionRear;
            Vector3 Anchor(float x, float z, AxleSuspension su, TyreData ty) =>
                new Vector3(x, ty.radius + su.travel * 0.6f, z);
            Wheels = new[]
            {
                new Wheel("FL", true, true, Anchor(-d.trackFront * 0.5f, frontZ, sf, spec.tyreFront), sf, spec.tyreFront),
                new Wheel("FR", true, false, Anchor(d.trackFront * 0.5f, frontZ, sf, spec.tyreFront), sf, spec.tyreFront),
                new Wheel("RL", false, true, Anchor(-d.trackRear * 0.5f, rearZ, sr, spec.tyreRear), sr, spec.tyreRear),
                new Wheel("RR", false, false, Anchor(d.trackRear * 0.5f, rearZ, sr, spec.tyreRear), sr, spec.tyreRear),
            };

            // преднатяг пружин: при статической нагрузке ход подвески ≈ 40%
            float g = -Physics.gravity.y;
            float frontLoad = m.mass * g * m.weightDistributionFront * 0.5f;
            float rearLoad = m.mass * g * (1f - m.weightDistributionFront) * 0.5f;
            foreach (var wh in Wheels)
            {
                float stat = wh.isFront ? frontLoad : rearLoad;
                wh.springPreload = stat - wh.susp.wheelRate * wh.susp.travel * 0.4f;
            }

            Drivetrain = new Drivetrain(spec.engine, spec.gearbox, spec.differential);

        }

        void Update()
        {
            if (Wheels == null) return;
            Input.Poll(SpeedMs, Time.deltaTime);
            visual?.Sync(this);
        }

        void FixedUpdate()
        {
            if (Wheels == null) return;
            float dt = Time.fixedDeltaTime;

            Vector3 v = Body.GetPointVelocity(Body.worldCenterOfMass);
            SpeedMs = v.magnitude;
            ForwardSpeedMs = Vector3.Dot(v, transform.forward);
            LocalAcceleration = transform.InverseTransformDirection((v - lastVelocity) / dt);
            lastVelocity = v;

            if (Input.ConsumeReset()) ResetRequested?.Invoke(this);
            HandleGears(dt);

            // --- 1. подвеска ---
            foreach (var w in Wheels) w.UpdateSuspension(transform, dt, groundMask);

            // --- 2. стабилизаторы поперечной устойчивости ---
            ApplyAntiRoll(Wheels[0], Wheels[1], Spec.suspensionFront.antiRollRate);
            ApplyAntiRoll(Wheels[2], Wheels[3], Spec.suspensionRear.antiRollRate);

            // --- руль с Аккерманом ---
            ApplySteering();

            // --- педали и помощники ---
            bool reverse = Drivetrain.Gear < 0;
            float throttle = reverse && DrivingAids.AutoGearbox ? Input.Brake : Input.Throttle;
            float brake = reverse && DrivingAids.AutoGearbox ? Input.Throttle : Input.Brake;
            throttle = ApplyTractionControl(throttle);
            ApplyBrakes(brake, dt);

            // --- 3. трансмиссия ---
            Drivetrain.Step(throttle, Wheels[2], Wheels[3], dt);
            Wheels[0].driveTorque = Wheels[1].driveTorque = 0f;

            // --- 4. шины ---
            foreach (var w in Wheels) w.UpdateTire(Body, transform, dt);

            // --- 5. аэродинамика ---
            ApplyAero(v);
        }

        void ApplyAntiRoll(Wheel left, Wheel right, float rate)
        {
            float diff = (left.grounded ? left.compression : 0f) - (right.grounded ? right.compression : 0f);
            float f = diff * rate;
            left.antiRollForce = left.grounded ? f : 0f;
            right.antiRollForce = right.grounded ? -f : 0f;
        }

        void ApplySteering()
        {
            var st = Spec.steering;
            float delta = Input.Steer * st.maxAngleDeg;
            float wb = Spec.dimensions.wheelbase;
            float track = Spec.dimensions.trackFront;
            float inner = delta, outer = delta;
            if (Mathf.Abs(delta) > 0.01f)
            {
                float r = wb / Mathf.Tan(Mathf.Abs(delta) * Mathf.Deg2Rad);
                float aIn = Mathf.Atan(wb / (r - track * 0.5f)) * Mathf.Rad2Deg;
                float aOut = Mathf.Atan(wb / (r + track * 0.5f)) * Mathf.Rad2Deg;
                inner = Mathf.Sign(delta) * Mathf.Lerp(Mathf.Abs(delta), aIn, st.ackermann);
                outer = Mathf.Sign(delta) * Mathf.Lerp(Mathf.Abs(delta), aOut, st.ackermann);
            }
            // поворот направо (delta > 0): правое колесо — внутреннее
            Wheels[0].steerAngleDeg = delta > 0f ? outer : inner;
            Wheels[1].steerAngleDeg = delta > 0f ? inner : outer;
        }

        float ApplyTractionControl(float throttle)
        {
            TcActive = false;
            if (DrivingAids.TcLevel <= 0 || Drivetrain.Gear <= 0) return throttle;
            float threshold = 0.16f - 0.03f * DrivingAids.TcLevel;   // 0.13 / 0.10 / 0.07
            float slip = Mathf.Max(Wheels[2].slipRatio, Wheels[3].slipRatio);
            if (slip > threshold && SpeedMs > 2f)
            {
                float cut = Mathf.Clamp01((slip - threshold) / 0.08f);
                TcActive = cut > 0.05f;
                return throttle * (1f - 0.9f * cut);
            }
            return throttle;
        }

        void ApplyBrakes(float brake, float dt)
        {
            var b = Spec.brakes;
            AbsActive = false;
            float absThreshold = DrivingAids.AbsLevel > 0 ? 0.20f - 0.035f * DrivingAids.AbsLevel : 99f;
            for (int i = 0; i < 4; i++)
            {
                var w = Wheels[i];
                float share = w.isFront ? b.biasFront : 1f - b.biasFront;
                float torque = brake * b.maxTorqueTotal * share * 0.5f;
                if (!w.isFront && Input.Handbrake) torque += b.handbrakeTorque;

                if (brake > 0.05f && w.slipRatio < -absThreshold && SpeedMs > 3f)
                {
                    absFactor[i] = Mathf.MoveTowards(absFactor[i], 0.25f, 25f * dt);
                    AbsActive = true;
                }
                else
                {
                    absFactor[i] = Mathf.MoveTowards(absFactor[i], 1f, 12f * dt);
                }
                w.brakeTorque = torque * (DrivingAids.AbsLevel > 0 ? absFactor[i] : 1f);
            }
        }

        void HandleGears(float dt)
        {
            if (Input.ConsumeShiftUp()) Drivetrain.ShiftUp();
            if (Input.ConsumeShiftDown()) Drivetrain.ShiftDown();
            if (!DrivingAids.AutoGearbox || Drivetrain.IsShifting) return;

            int gear = Drivetrain.Gear;
            float limiter = Drivetrain.LimiterRpm;

            // задний ход: остановились и держим тормоз
            if (gear >= 1 && SpeedMs < 0.8f && Input.Brake > 0.5f && Input.Throttle < 0.1f)
            {
                reverseTimer += dt;
                if (reverseTimer > 0.5f) { Drivetrain.RequestGear(-1); reverseTimer = 0f; }
                return;
            }
            if (gear < 0 && SpeedMs < 0.8f && Input.Throttle > 0.5f && Input.Brake < 0.1f)
            {
                reverseTimer += dt;
                if (reverseTimer > 0.3f) { Drivetrain.RequestGear(1); reverseTimer = 0f; }
                return;
            }
            reverseTimer = 0f;
            if (gear == 0) { Drivetrain.RequestGear(1); return; }
            if (gear < 0) return;

            shiftCooldown -= dt;
            if (shiftCooldown > 0f) return;

            // решение по оборотам колёс (обороты двигателя сразу после переключения ещё «старые»)
            float driven = 0.5f * (Wheels[2].angularVelocity + Wheels[3].angularVelocity);
            float toRpm = 60f / (2f * Mathf.PI);
            float rpmNow = driven * Drivetrain.GearRatio(gear) * toRpm;
            bool wheelspin = Mathf.Max(Wheels[2].slipRatio, Wheels[3].slipRatio) > 0.25f;
            if (gear < Drivetrain.GearCount && rpmNow > limiter * 0.965f && !wheelspin)
            {
                if (Drivetrain.RequestGear(gear + 1)) shiftCooldown = 0.3f;
            }
            else if (gear > 1)
            {
                float rpmLower = driven * Drivetrain.GearRatio(gear - 1) * toRpm;
                float downAt = Input.Brake > 0.2f ? limiter * 0.80f : limiter * 0.62f;
                if (rpmLower < downAt && Drivetrain.RequestGear(gear - 1)) shiftCooldown = 0.25f;
            }
        }

        void ApplyAero(Vector3 v)
        {
            var a = Spec.aero;
            float fwdSpeed = Mathf.Max(0f, Vector3.Dot(v, transform.forward));
            float q = 0.5f * a.airDensity;
            Drag = q * a.dragArea * v.sqrMagnitude;
            if (v.sqrMagnitude > 0.01f)
                Body.AddForceAtPosition(-v.normalized * Drag, transform.TransformPoint(new Vector3(0f, Spec.mass.cgHeight + 0.1f, 0f)));

            Downforce = q * a.downforceArea * fwdSpeed * fwdSpeed;
            float wb = Spec.dimensions.wheelbase;
            Vector3 front = transform.TransformPoint(new Vector3(0f, 0.3f, wb * 0.5f));
            Vector3 rear = transform.TransformPoint(new Vector3(0f, 0.3f, -wb * 0.5f));
            Body.AddForceAtPosition(-transform.up * Downforce * a.balanceFront, front);
            Body.AddForceAtPosition(-transform.up * Downforce * (1f - a.balanceFront), rear);
        }

        /// <summary>Переставить машину (старт или возврат на трассу).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Body.isKinematic = true;
            transform.SetPositionAndRotation(position, rotation);
            Body.position = position;
            Body.rotation = rotation;
            Body.isKinematic = false;
            Body.AddForce(-Body.GetPointVelocity(Body.worldCenterOfMass), ForceMode.VelocityChange);
            Body.AddTorque(-Body.angularVelocity, ForceMode.VelocityChange);
            lastVelocity = Vector3.zero;
            foreach (var w in Wheels) w.ResetState();
            Drivetrain.Reset();
        }
    }
}
