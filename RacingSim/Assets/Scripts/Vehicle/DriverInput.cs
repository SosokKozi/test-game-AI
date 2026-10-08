using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>
    /// Ввод водителя (старая Input Manager — работает «из коробки»):
    ///   W/↑ — газ, S/↓ — тормоз, A/D/←/→ — руль (также ось Horizontal геймпада),
    ///   E / Left Shift — передача вверх, Q / Left Ctrl — вниз, Space — ручник,
    ///   R — вернуть на трассу.
    /// Клавиатурный руль сглаживается и уменьшается с ростом скорости.
    /// </summary>
    public class DriverInput
    {
        public float Steer { get; private set; }     // -1..1
        public float Throttle { get; private set; }  // 0..1
        public float Brake { get; private set; }     // 0..1
        public bool Handbrake { get; private set; }
        public bool ShiftUpPressed { get; private set; }
        public bool ShiftDownPressed { get; private set; }
        public bool ResetPressed { get; private set; }

        public bool Enabled = true;
        public float SteerSpeed = 2.2f;     // 1/с — скорость поворота руля с клавиатуры
        public float ReturnSpeed = 3.5f;
        public float PedalSpeed = 6f;

        /// <summary>Вызывать из Update.</summary>
        public void Poll(float speedMs, float dt)
        {
            if (!Enabled)
            {
                Steer = Mathf.MoveTowards(Steer, 0f, ReturnSpeed * dt);
                Throttle = 0f;
                Brake = 1f;
                return;
            }

            float keySteer = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) keySteer -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keySteer += 1f;

            float axis = 0f;
            try { axis = Input.GetAxisRaw("Horizontal"); } catch (System.ArgumentException) { }
            bool analog = Mathf.Abs(axis) > 0.05f && keySteer == 0f && !AnyArrowOrAd();

            // ограничение выкручивания на скорости (только клавиатура)
            float speedLimit = Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(15f, 70f, speedMs));
            if (analog)
            {
                Steer = axis;
            }
            else if (keySteer != 0f)
            {
                float target = keySteer * speedLimit;
                float rate = Mathf.Sign(target) != Mathf.Sign(Steer) ? ReturnSpeed : SteerSpeed;
                Steer = Mathf.MoveTowards(Steer, target, rate * dt);
            }
            else
            {
                Steer = Mathf.MoveTowards(Steer, 0f, ReturnSpeed * dt);
            }

            float t = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f;
            float b = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f;
            Throttle = Mathf.MoveTowards(Throttle, t, PedalSpeed * dt);
            Brake = Mathf.MoveTowards(Brake, b, PedalSpeed * 1.5f * dt);
            Handbrake = Input.GetKey(KeyCode.Space);

            ShiftUpPressed |= Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.LeftShift);
            ShiftDownPressed |= Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftControl);
            ResetPressed |= Input.GetKeyDown(KeyCode.R);
        }

        static bool AnyArrowOrAd() =>
            Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) ||
            Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);

        /// <summary>Нажатия «одним кадром» забираются в FixedUpdate.</summary>
        public bool ConsumeShiftUp() { bool v = ShiftUpPressed; ShiftUpPressed = false; return v; }
        public bool ConsumeShiftDown() { bool v = ShiftDownPressed; ShiftDownPressed = false; return v; }
        public bool ConsumeReset() { bool v = ResetPressed; ResetPressed = false; return v; }
    }
}
