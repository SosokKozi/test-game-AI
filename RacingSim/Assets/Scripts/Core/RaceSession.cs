using RacingSim.Track;
using RacingSim.Vehicle;
using UnityEngine;

namespace RacingSim.Core
{
    /// <summary>
    /// Игровая сессия: меню выбора трассы/машины/помощников, построение трассы, спавн машины,
    /// хронометраж и HUD (IMGUI — без зависимостей от пакетов UI).
    /// </summary>
    public class RaceSession : MonoBehaviour
    {
        public static readonly string[] TrackIds = { "Monza", "Spa", "MountPanorama" };
        public static readonly string[] CarIds = { "AstonMartinVantageGT3", "Ferrari296GT3", "BMWM4GT3" };

        int trackSel, carSel;
        bool inMenu = true;
        bool showTelemetry = true;
        string error;

        TrackData track;
        GameObject trackObject;
        VehicleController car;
        LapTimer timer;
        CameraRig cameraRig;
        Light sun;

        GUIStyle big, mid, small, box;

        void Start()
        {
            // физика: 500 Гц — шины с длиной релаксации и жёсткая подвеска требуют малого шага
            Time.fixedDeltaTime = 1f / 500f;
            Time.maximumDeltaTime = 1f / 10f;
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 4;

            var camGo = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();
            cameraRig = camGo.GetComponent<CameraRig>();
            if (cameraRig == null) cameraRig = camGo.AddComponent<CameraRig>();
            camGo.transform.SetPositionAndRotation(new Vector3(0, 300, -600), Quaternion.Euler(25, 0, 0));

            sun = FindAnyObjectByType<Light>();
            if (sun == null || sun.type != LightType.Directional)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            QualitySettings.shadowDistance = 250f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 600f;
            RenderSettings.fogEndDistance = 3500f;
            RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.9f);
        }

        void StartRace()
        {
            error = null;
            try
            {
                if (trackObject != null) Destroy(trackObject);
                if (car != null) Destroy(car.gameObject);

                track = TrackData.Load(TrackIds[trackSel]);
                trackObject = TrackBuilder.Build(track);

                var spec = CarSpec.Load(CarIds[carSel]);
                var go = new GameObject(spec.displayName);
                go.AddComponent<Rigidbody>();
                car = go.AddComponent<VehicleController>();
                car.Init(spec);
                car.ResetRequested = ResetToTrack;
                var audio = go.AddComponent<EngineAudio>();
                audio.car = car;
                audio.cylinders = spec.engine.layout.Contains("V8") ? 8 : 6;

                timer = new LapTimer(track);
                PlaceOnTrack(track.length - 35f);
                cameraRig.target = car;
                cameraRig.SnapBehind();
                inMenu = false;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                Debug.LogException(e);
            }
        }

        void PlaceOnTrack(float distance)
        {
            track.Sample(distance, out var pos, out var fwd, out var up);
            car.Teleport(pos + up * 0.25f, Quaternion.LookRotation(fwd, up));
            timer.Invalidate();
        }

        void ResetToTrack(VehicleController c)
        {
            int i = track.NearestIndex(c.transform.position, timer.Index);
            PlaceOnTrack(track.distance[i]);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && car != null) inMenu = !inMenu;
            if (Input.GetKeyDown(KeyCode.H)) showTelemetry = !showTelemetry;
            if (Input.GetKeyDown(KeyCode.T)) DrivingAids.TcLevel = (DrivingAids.TcLevel + 1) % 4;
            if (Input.GetKeyDown(KeyCode.Y)) DrivingAids.AbsLevel = (DrivingAids.AbsLevel + 1) % 4;
            if (Input.GetKeyDown(KeyCode.G)) DrivingAids.AutoGearbox = !DrivingAids.AutoGearbox;
            if (car != null) car.Input.Enabled = !inMenu;
            Time.timeScale = inMenu && car != null ? 0f : 1f;
        }

        void FixedUpdate()
        {
            if (car != null && timer != null) timer.Update(car.transform.position, Time.fixedDeltaTime);
        }

        // ------------------------------------------------------------------ UI

        void InitStyles()
        {
            if (big != null) return;
            big = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            mid = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            box = new GUIStyle(GUI.skin.box) { fontSize = 16 };
            big.normal.textColor = mid.normal.textColor = small.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            InitStyles();
            if (inMenu) DrawMenu();
            else if (car != null) DrawHud();
        }

        void DrawMenu()
        {
            float w = 560, h = 470;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.Box(r, "");
            GUI.Box(r, "");
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 15, w - 40, h - 30));
            GUILayout.Label("RacingSim — GT3", big);
            GUILayout.Space(10);
            GUILayout.Label("Трасса", mid);
            trackSel = GUILayout.Toolbar(trackSel, new[] { "Monza", "Spa-Francorchamps", "Mount Panorama" }, GUILayout.Height(32));
            GUILayout.Label("Автомобиль", mid);
            carSel = GUILayout.Toolbar(carSel, new[] { "Aston Martin Vantage", "Ferrari 296", "BMW M4" }, GUILayout.Height(32));
            GUILayout.Space(8);
            GUILayout.Label("Помощники", mid);
            DrivingAids.AutoGearbox = GUILayout.Toggle(DrivingAids.AutoGearbox, " Автоматическое переключение передач (G)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("ABS (Y):", GUILayout.Width(110));
            DrivingAids.AbsLevel = GUILayout.Toolbar(DrivingAids.AbsLevel, new[] { "выкл", "1", "2", "3" });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Трекшн-контроль (T):", GUILayout.Width(110));
            DrivingAids.TcLevel = GUILayout.Toolbar(DrivingAids.TcLevel, new[] { "выкл", "1", "2", "3" });
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            if (GUILayout.Button(car == null ? "СТАРТ" : "НОВАЯ СЕССИЯ (перестроить трассу/машину)", GUILayout.Height(44))) StartRace();
            if (car != null && GUILayout.Button("Продолжить (Esc)", GUILayout.Height(30))) inMenu = false;
            if (error != null) GUILayout.Label("<color=red>Ошибка: " + error + "</color>", new GUIStyle(small) { richText = true, wordWrap = true });
            GUILayout.FlexibleSpace();
            GUILayout.Label("W/S — газ/тормоз · A/D — руль · E/Q — передачи · Space — ручник · R — на трассу · C — камера · H — телеметрия", new GUIStyle(small) { wordWrap = true });
            GUILayout.EndArea();
        }

        void DrawHud()
        {
            var dt = car.Drivetrain;
            // скорость и передача
            string gear = dt.Gear < 0 ? "R" : dt.Gear == 0 ? "N" : dt.Gear.ToString();
            var r = new Rect(Screen.width - 260, Screen.height - 170, 240, 150);
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x, r.y + 5, r.width, 50), $"{car.SpeedKmh:0}", big);
            GUI.Label(new Rect(r.x, r.y + 52, r.width, 20), "км/ч", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(r.x, r.y + 70, r.width, 50), gear, new GUIStyle(big) { fontSize = 36, normal = { textColor = dt.LimiterActive ? Color.red : Color.white } });
            // полоса оборотов
            float rpmFrac = Mathf.Clamp01(dt.EngineRpm / dt.LimiterRpm);
            var bar = new Rect(r.x + 10, r.y + 125, r.width - 20, 14);
            GUI.Box(bar, "");
            var old = GUI.color;
            GUI.color = rpmFrac > 0.95f ? Color.red : rpmFrac > 0.85f ? Color.yellow : Color.green;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * rpmFrac, bar.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(r.x + 10, r.y + 105, 200, 20), $"{dt.EngineRpm:0} об/мин", small);

            // индикаторы помощников
            string aids = (car.AbsActive ? "<color=orange>ABS</color> " : "ABS ") + (car.TcActive ? "<color=orange>TC</color>" : "TC");
            GUI.Label(new Rect(r.x + 160, r.y + 105, 80, 20), $"{aids}", new GUIStyle(small) { richText = true });

            // время круга
            var lt = new Rect(20, 20, 300, 140);
            GUI.Box(lt, "");
            GUI.Label(new Rect(lt.x + 10, lt.y + 5, 280, 25), $"{track.displayName}", small);
            GUI.Label(new Rect(lt.x + 10, lt.y + 28, 280, 28), $"Круг {timer.Lap + 1}   {LapTimer.Format(timer.Started ? timer.CurrentLapTime : -1f)}", mid);
            GUI.Label(new Rect(lt.x + 10, lt.y + 58, 280, 22), $"Последний: {LapTimer.Format(timer.LastLapTime)}", small);
            GUI.Label(new Rect(lt.x + 10, lt.y + 78, 280, 22), $"Лучший:    {LapTimer.Format(timer.BestLapTime)}", small);
            string s = "";
            for (int i = 0; i < 3; i++) s += $"S{i + 1} {(timer.CurrentSectors[i] < 0 ? "--.---" : timer.CurrentSectors[i].ToString("0.000"))}  ";
            GUI.Label(new Rect(lt.x + 10, lt.y + 100, 280, 22), s, small);

            if (showTelemetry) DrawTelemetry();
        }

        void DrawTelemetry()
        {
            var r = new Rect(20, Screen.height - 250, 420, 230);
            GUI.Box(r, "");
            float y = r.y + 8;
            void Line(string text) { GUI.Label(new Rect(r.x + 10, y, r.width - 20, 20), text, small); y += 19; }
            var a = car.LocalAcceleration / 9.81f;
            Line($"{car.Spec.displayName}   Прижим {car.Downforce / 9.81f:0} кг   Сопротивление {car.Drag:0} Н");
            Line($"Перегрузки: попер. {a.x:+0.00;-0.00} g   прод. {a.z:+0.00;-0.00} g");
            Line($"ABS {DrivingAids.AbsLevel}  TC {DrivingAids.TcLevel}  Авто-КПП {(DrivingAids.AutoGearbox ? "вкл" : "выкл")}   Сцепл. {(car.Drivetrain.ClutchLocked ? "замкн." : car.Drivetrain.ClutchEngagement.ToString("0.00"))}");
            Line("Колесо  Нагрузка   Скольж.   Увод°   Покрытие");
            foreach (var w in car.Wheels)
                Line($"{w.name}      {w.load,6:0} Н   {w.slipRatio,6:0.000}   {w.SlipAngleDeg,6:0.0}    {(w.grounded ? w.surface.type.ToString() : "в воздухе")}");
            Line($"Дистанция {timer.Distance:0} / {track.length:0} м   Высота {car.transform.position.y + track.baseAltitude:0} м н.у.м.");
        }
    }
}
