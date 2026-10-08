using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>
    /// Колесо: подвеска (рейкаст), шина (Пасейка + длина релаксации) и вращение колеса.
    /// Не MonoBehaviour — им управляет VehicleController в строгом порядке внутри FixedUpdate.
    /// </summary>
    public class Wheel
    {
        public readonly string name;
        public readonly bool isFront;
        public readonly bool isLeft;
        public readonly Vector3 localAnchor;   // верх хода подвески в координатах машины
        public readonly float radius;
        public readonly AxleSuspension susp;
        public readonly TyreData tyre;
        public readonly TireModel tire;

        // управляющие воздействия (задаются контроллером каждый шаг)
        public float steerAngleDeg;
        public float driveTorque;
        public float brakeTorque;
        public float extraInertia;    // приведённая инерция двигателя/трансмиссии
        public float springPreload;   // Н

        // состояние
        public bool grounded;
        public float compression;
        float prevCompression;
        public float load;            // Fz, Н
        public float antiRollForce;
        public Vector3 contactPoint;
        public Vector3 contactNormal = Vector3.up;
        public Vector3 contactForward = Vector3.forward;
        public Vector3 contactRight = Vector3.right;
        public SurfaceInfo surface;
        public float hitDistance;

        public float angularVelocity; // рад/с
        public float slipRatio;
        public float tanSlipAngle;
        public float combinedSlip;
        public float longitudinalVelocity;
        public float lateralVelocity;
        public float forceLong;
        public float forceLat;
        public float spinAngle;       // для визуализации, градусы

        public float SlipAngleDeg => Mathf.Atan(tanSlipAngle) * Mathf.Rad2Deg;
        public float SurfaceSpeed => angularVelocity * radius;

        const float MinRelaxSpeed = 0.5f;

        public Wheel(string name, bool isFront, bool isLeft, Vector3 localAnchor, AxleSuspension susp, TyreData tyre)
        {
            this.name = name;
            this.isFront = isFront;
            this.isLeft = isLeft;
            this.localAnchor = localAnchor;
            this.susp = susp;
            this.tyre = tyre;
            radius = tyre.radius;
            tire = new TireModel(tyre);
        }

        public float RayLength => susp.travel + radius;

        /// <summary>Шаг 1: рейкаст и сила подвески (без стабилизатора).</summary>
        public void UpdateSuspension(Transform car, float dt, int layerMask)
        {
            Vector3 anchor = car.TransformPoint(localAnchor);
            Vector3 down = -car.up;
            if (Physics.Raycast(anchor, down, out RaycastHit hit, RayLength, layerMask, QueryTriggerInteraction.Ignore))
            {
                grounded = true;
                hitDistance = hit.distance;
                contactPoint = hit.point;
                contactNormal = hit.normal;
                surface = SurfaceInfo.Get(hit.collider);
                compression = RayLength - hit.distance;
                if (surface.bumpiness > 0f)
                {
                    float n = Mathf.PerlinNoise(hit.point.x * 1.7f, hit.point.z * 1.7f) - 0.5f;
                    compression += n * 2f * surface.bumpiness;
                }
            }
            else
            {
                grounded = false;
                hitDistance = RayLength;
                compression = 0f;
                surface = SurfaceInfo.Default;
            }

            // ограничение — чтобы первое касание после телепорта не давало «выстрел» демпфера
            float compVel = Mathf.Clamp((compression - prevCompression) / dt, -3f, 3f);
            prevCompression = compression;

            if (!grounded)
            {
                load = 0f;
                return;
            }

            float spring = springPreload + susp.wheelRate * compression;
            if (compression > susp.travel)
                spring += susp.bumpStopRate * (compression - susp.travel);
            float damper = compVel > 0f ? susp.bumpDamping * compVel : susp.reboundDamping * compVel;
            load = spring + damper;
        }

        /// <summary>Шаг 2: силы подвески и шины, интегрирование вращения колеса.</summary>
        public void UpdateTire(Rigidbody rb, Transform car, float dt)
        {
            float totalInertia = tyre.inertia + extraInertia;

            if (!grounded)
            {
                load = 0f;
                forceLong = forceLat = 0f;
                combinedSlip = 0f;
                IntegrateSpin(driveTorque, totalInertia, dt);
                return;
            }

            load = Mathf.Max(0f, load + antiRollForce);
            rb.AddForceAtPosition(car.up * load, contactPoint);

            // локальная система координат пятна контакта
            Vector3 n = contactNormal;
            Vector3 wheelFwd = Quaternion.AngleAxis(steerAngleDeg, car.up) * car.forward;
            Vector3 fwd = (wheelFwd - n * Vector3.Dot(wheelFwd, n)).normalized;
            Vector3 right = Vector3.Cross(n, fwd);
            contactForward = fwd;
            contactRight = right;

            Vector3 v = rb.GetPointVelocity(contactPoint);
            float vx = Vector3.Dot(v, fwd);
            float vy = Vector3.Dot(v, right);
            longitudinalVelocity = vx;
            lateralVelocity = vy;

            // динамика скольжения с длиной релаксации (неявная схема — устойчива на любой скорости)
            float k = dt / tyre.relaxationLength;
            float speedAbs = Mathf.Max(Mathf.Abs(vx), MinRelaxSpeed);
            slipRatio = (slipRatio + k * (angularVelocity * radius - vx)) / (1f + k * speedAbs);
            tanSlipAngle = (tanSlipAngle + k * (-vy)) / (1f + k * speedAbs);
            slipRatio = Mathf.Clamp(slipRatio, -3f, 3f);
            tanSlipAngle = Mathf.Clamp(tanSlipAngle, -3f, 3f);

            tire.ComputeForces(slipRatio, tanSlipAngle, load, surface.grip, out float fx, out float fy, out combinedSlip);

            // сопротивление качению
            float rr = (tyre.rollingResistance + surface.rollingDrag) * load * Mathf.Clamp(vx, -1f, 1f);

            forceLong = fx;
            forceLat = fy;
            rb.AddForceAtPosition(fwd * (fx - rr) + right * fy, contactPoint);

            IntegrateSpin(driveTorque - fx * radius, totalInertia, dt);
        }

        void IntegrateSpin(float torque, float inertia, float dt)
        {
            angularVelocity += torque / inertia * dt;

            // тормоз — сила трения: не может раскрутить колесо в обратную сторону
            float brakeDelta = brakeTorque / inertia * dt;
            if (Mathf.Abs(angularVelocity) <= brakeDelta) angularVelocity = 0f;
            else angularVelocity -= Mathf.Sign(angularVelocity) * brakeDelta;

            spinAngle = Mathf.Repeat(spinAngle + angularVelocity * Mathf.Rad2Deg * dt, 360f);
        }

        /// <summary>Положение центра колеса в мировых координатах (для визуализации).</summary>
        public Vector3 WheelCenter(Transform car)
        {
            Vector3 anchor = car.TransformPoint(localAnchor);
            float dist = grounded ? Mathf.Max(hitDistance - radius, 0f) : susp.travel;
            return anchor - car.up * dist;
        }

        public void ResetState()
        {
            angularVelocity = slipRatio = tanSlipAngle = 0f;
            compression = prevCompression = 0f;
            load = 0f;
        }
    }
}
