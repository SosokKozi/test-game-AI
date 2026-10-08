using System;
using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>
    /// Технические данные автомобиля. Загружаются из Resources/Cars/*.json (JsonUtility).
    /// Все величины в СИ: кг, м, Н, Н·м, об/мин указаны явно.
    /// </summary>
    [Serializable]
    public class CarSpec
    {
        public string id;
        public string displayName;
        public string manufacturer;
        public string notes;
        public string bodyStyle = "frontEngine"; // frontEngine | midEngine — влияет на процедурный кузов
        public float[] bodyColor = { 0.1f, 0.4f, 0.3f };
        public float[] accentColor = { 0.9f, 0.9f, 0.9f };

        public Dimensions dimensions = new Dimensions();
        public MassData mass = new MassData();
        public EngineData engine = new EngineData();
        public GearboxData gearbox = new GearboxData();
        public DiffData differential = new DiffData();
        public BrakeData brakes = new BrakeData();
        public AxleSuspension suspensionFront = new AxleSuspension();
        public AxleSuspension suspensionRear = new AxleSuspension();
        public TyreData tyreFront = new TyreData();
        public TyreData tyreRear = new TyreData();
        public AeroData aero = new AeroData();
        public SteeringData steering = new SteeringData();

        public static CarSpec Load(string id)
        {
            var asset = Resources.Load<TextAsset>("Cars/" + id);
            if (asset == null) throw new ArgumentException("Car spec not found: " + id);
            return JsonUtility.FromJson<CarSpec>(asset.text);
        }

        public Color BodyColor => ToColor(bodyColor);
        public Color AccentColor => ToColor(accentColor);

        static Color ToColor(float[] c) =>
            c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) : Color.gray;
    }

    [Serializable]
    public class Dimensions
    {
        public float length = 4.6f;
        public float width = 2.0f;
        public float height = 1.2f;
        public float wheelbase = 2.7f;
        public float trackFront = 1.7f;
        public float trackRear = 1.65f;
        public float frontOverhang = 0.95f; // от передней оси до носа
    }

    [Serializable]
    public class MassData
    {
        public float mass = 1300f;               // с пилотом и топливом
        public float weightDistributionFront = 0.48f;
        public float cgHeight = 0.42f;
        public float inertiaScale = 0.85f;       // <1 — масса сосредоточена ближе к центру
    }

    [Serializable]
    public class EngineData
    {
        public string layout = "V8 twin-turbo";
        public float[] torqueRpm = { 1000, 3000, 5000, 7000 };
        public float[] torqueNm = { 300, 600, 650, 550 };
        public float idleRpm = 1500f;
        public float limiterRpm = 7500f;
        public float inertia = 0.18f;            // кг·м², маховик + коленвал
        public float frictionTorque = 25f;       // Н·м при холостых
        public float frictionPerRpm = 0.006f;    // Н·м на об/мин (торможение двигателем)
        public float throttleResponse = 12f;     // 1/с — инерция турбин/дросселя
    }

    [Serializable]
    public class GearboxData
    {
        public float[] ratios = { 3.0f, 2.2f, 1.7f, 1.4f, 1.2f, 1.0f };
        public float reverseRatio = 3.0f;
        public float finalDrive = 3.5f;
        public float shiftTime = 0.06f;          // с, секвентальная КПП
        public float efficiency = 0.92f;
        public float clutchMaxTorque = 1100f;
    }

    [Serializable]
    public class DiffData
    {
        public float preload = 80f;      // Н·м
        public float powerRamp = 0.35f;  // доля входного момента на блокировку под газом
        public float coastRamp = 0.20f;  // при торможении двигателем
    }

    [Serializable]
    public class BrakeData
    {
        public float maxTorqueTotal = 11000f;    // Н·м суммарно на 4 колеса
        public float biasFront = 0.58f;
        public float handbrakeTorque = 2500f;
    }

    [Serializable]
    public class AxleSuspension
    {
        public float wheelRate = 120000f;        // Н/м (пружина с учётом передаточного отношения)
        public float bumpDamping = 6500f;        // Н·с/м
        public float reboundDamping = 9000f;
        public float travel = 0.10f;             // м
        public float antiRollRate = 60000f;      // Н/м разницы ходов
        public float bumpStopRate = 900000f;
    }

    [Serializable]
    public class TyreData
    {
        public string size = "30/68-18";
        public float radius = 0.34f;
        public float width = 0.30f;
        public float inertia = 1.3f;             // кг·м², колесо + диск + ступица
        public float muLongitudinal = 1.75f;
        public float muLateral = 1.70f;
        public float peakSlipRatio = 0.10f;
        public float peakSlipAngleDeg = 7.0f;
        public float shapeC = 1.6f;              // форма кривой Пасейки
        public float falloffE = -0.4f;           // спад после пика (меньше — резче)
        public float loadSensitivity = 0.12f;    // падение μ с ростом нагрузки
        public float nominalLoad = 4000f;        // Н
        public float relaxationLength = 0.35f;   // м
        public float rollingResistance = 0.012f;
    }

    [Serializable]
    public class AeroData
    {
        public float dragArea = 0.85f;           // Cd·A, м²
        public float downforceArea = 2.4f;       // Cl·A, м²
        public float balanceFront = 0.42f;       // доля прижима на передней оси
        public float airDensity = 1.225f;
    }

    [Serializable]
    public class SteeringData
    {
        public float maxAngleDeg = 24f;          // угол колёс при полном выкручивании
        public float ackermann = 0.6f;
        public float lockToLockDeg = 540f;       // для руля
    }
}
