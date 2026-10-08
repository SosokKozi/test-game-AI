# RacingSim — заметки для Claude Code

Учебный GT3-симулятор на Unity 6 (Monza, Spa, Mount Panorama; Aston Martin Vantage GT3, Ferrari 296 GT3, BMW M4 GT3).
Пользователь общается по-русски; комментарии в коде и README — на русском.

## Архитектура
- Сцены не используются: `Core/GameBootstrap.cs` создаёт `RaceSession` при Play в любой сцене.
- Всё строится в рантайме из JSON в `Assets/Resources` (`CarSpec.Load`, `TrackData.Load`), поэтому
  в репозитории нет .unity/.prefab/.meta, которые нельзя проверить без редактора.
- Физика машины — собственная (не WheelCollider): `Wheel` (рейкаст-подвеска + шина с длиной релаксации),
  `TireModel` (Пасейка, комбинированное скольжение), `Drivetrain` (двигатель, сцепление, КПП, LSD),
  `VehicleController` (порядок шага, ABS/TC, аэро). Шаг физики 500 Гц задаётся в `RaceSession.Start`.
- Машина на слое 2 (Ignore Raycast), чтобы лучи подвески не попадали в собственный кузов.
- UI — IMGUI (`OnGUI`), без зависимостей от пакетов.

## Проверки
- Компиляция без Unity невозможна напрямую; API должен оставаться совместимым с Unity 2021.3+ и Unity 6
  (не использовать `Rigidbody.velocity`/`linearVelocity`, `PhysicMaterial`/`PhysicsMaterial`, `FindObjectOfType`).
- `tools/physics-test`: `dotnet run` — продольный тест физики на реальных классах Vehicle с заглушкой UnityEngine.
  Запускайте после любых изменений шин/трансмиссии/JSON машин.
- `tools/trackgen/generate_tracks.py --plot out.png` — после правок трасс проверьте замыкание, длину,
  `min_gap_m` (нет самопересечений) и картинку.

## Данные
- Характеристики машин приближённые (BoP). Единицы СИ, см. комментарии в `CarSpec.cs`.
- Формат трассы: `points` = [x, высота, z, ширина, вираж°, зона безопасности] × N, шаг 2 м, X — восток, Z — север.
