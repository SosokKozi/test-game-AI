# RacingSim — учебный GT3-симулятор на Unity

Некоммерческий учебный проект: гонки на GT3-машинах с упором на реалистичную физику.

| Трассы | Автомобили |
|---|---|
| Autodromo Nazionale **Monza** (5 793 м, ~10 м перепад) | **Aston Martin Vantage AMR GT3 Evo** (4.0 V8 TT, 1330 кг) |
| Circuit de **Spa-Francorchamps** (7 004 м, ~100 м перепад, Eau Rouge) | **Ferrari 296 GT3** (2.9 V6 TT, mid-engine, 1270 кг) |
| **Mount Panorama**, Bathurst (6 213 м, ~173 м перепад) | **BMW M4 GT3** (3.0 I6 TT, база 2917 мм, 1300 кг) |

## Быстрый старт (Windows, всё на диске F:)

1. Откройте **PowerShell от имени администратора** и выполните:
   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass -Force
   irm https://raw.githubusercontent.com/SosokKozi/test-game-AI/claude/adoring-albattani-93trad/setup/install-windows.ps1 -OutFile $env:TEMP\install-rs.ps1
   & $env:TEMP\install-rs.ps1            # добавьте -WithRider, если нужен JetBrains Rider
   ```
   Скрипт ставит в `F:\GameDev`: Git (+LFS), Unity Hub, **Unity 6**, VS Code с расширениями C#/Unity/Python,
   .NET 8 SDK, Python 3.12 (+numpy/scipy/matplotlib/networkx/requests), Blender. Он же переносит кэши
   (пакеты Unity, Asset Store, pip, NuGet, npm) на F: и клонирует проект в `F:\GameDev\Projects\test-game-AI`.
   Скрипт можно запускать повторно — уже установленное пропускается.
2. Войдите в **Unity Hub** и активируйте бесплатную лицензию Personal. В Hub → Preferences → Locations
   проверьте, что и *Installs*, и *Downloads* указывают на диск F:.
3. Unity Hub → **Add project from disk** → `F:\GameDev\Projects\test-game-AI\RacingSim`.
4. Нажмите **Play**: появится меню выбора трассы, машины и помощников. Сцены собирать не нужно —
   `GameBootstrap` запускает игру в любой (даже пустой) сцене.

> На диске C: всё равно останутся небольшие служебные файлы (настройки Unity Hub в AppData, сам winget) —
> это поведение Windows, перенести их нельзя.

## Управление

| Клавиша | Действие |
|---|---|
| W / ↑ | газ |
| S / ↓ | тормоз (при остановке — задний ход в авто-режиме) |
| A, D / ←, → | руль (сглаживается и уменьшается с ростом скорости); ось *Horizontal* геймпада |
| E / Left Shift, Q / Left Ctrl | передача вверх / вниз |
| Space | ручник |
| R | вернуть на трассу |
| C | камера: преследование → кокпит → капот → ТВ |
| G / Y / T | авто-КПП / уровень ABS / уровень трекшн-контроля |
| H | телеметрия (нагрузки на колёса, скольжение, угол увода, перегрузки, прижим) |
| Esc | меню |

## Физика

Всё в `RacingSim/Assets/Scripts/Vehicle`. Шаг физики **500 Гц** (`Time.fixedDeltaTime = 0.002`).

* **Шины** (`TireModel.cs`) — Magic Formula Пасейки с нормализованным комбинированным скольжением
  (эллипс трения): продольная и боковая сила делят общий запас сцепления. Учтена чувствительность μ к
  нагрузке (сильнее нагруженная шина держит хуже в пересчёте на килограмм) и **длина релаксации**
  (`Wheel.cs`): скольжение нарастает не мгновенно, что даёт устойчивость на малых скоростях и реалистичный
  отклик на руль.
* **Подвеска** — рейкаст на каждое колесо: пружина с преднатягом, раздельные демпферы сжатия/отбоя,
  отбойник хода, стабилизаторы поперечной устойчивости. Перенос нагрузки при торможении/в поворотах
  получается сам собой из сил подвески.
* **Трансмиссия** (`Drivetrain.cs`) — кривая момента двигателя по реальным данным, инерция маховика,
  торможение двигателем, ограничитель оборотов, лаг турбин, автоматическое сцепление для старта,
  секвентальная 6-ступенчатая КПП (время переключения 50–60 мс) и **самоблокирующийся дифференциал**
  (преднатяг + рампы под газом и при сбросе).
* **Тормоза** — максимальный момент и баланс по осям, ABS (3 уровня), трекшн-контроль (3 уровня).
* **Аэродинамика** — сопротивление `Cd·A`, прижимная сила `Cl·A` с аэробалансом по осям.
* **Покрытия** — асфальт, поребрики (неровные), трава и гравий (меньше сцепление, большее сопротивление).

Все параметры машин — в `Assets/Resources/Cars/*.json` (масса, развесовка, высота ЦТ, кривая момента,
передаточные числа, жёсткости пружин и т.д.). Данные приближённые: реальные GT3 зависят от
Balance of Performance, а точные настройки команд закрыты.

### Тест физики без Unity

```powershell
cd tools\physics-test
dotnet run
```
Прогоняет настоящие `TireModel`/`Drivetrain`/`Wheel` в продольной модели и печатает разгон 0–100 и 0–200,
максимальную скорость и тормозной путь. Текущие результаты:

| Машина | 0–100 км/ч | 0–200 км/ч | V max | 280→0 км/ч |
|---|---|---|---|---|
| Aston Martin Vantage GT3 | 3.0 с | 8.7 с | 282 км/ч | 139 м |
| Ferrari 296 GT3 | 2.8 с | 7.7 с | 282 км/ч | 134 м |
| BMW M4 GT3 | 3.5 с | 8.8 с | 282 км/ч | 135 м |

Боковая динамика (повороты) требует Rigidbody и проверяется только в Unity.

## Трассы

Трассы строятся в рантайме (`Scripts/Track/TrackBuilder.cs`) из центральной линии
`Assets/Resources/Tracks/*.json`: асфальт с виражами, разметка, красно-белые поребрики в поворотах,
трава/гравий, отбойники, стартовый портал, рельеф местности (Unity Terrain) и лес.

Центральные линии получены двумя способами (формат одинаковый):

1. **`tools/trackgen/generate_tracks.py`** (уже сгенерировано и лежит в репозитории) — трасса задана
   последовательностью поворотов и прямых по реальным данным (Rettifilo → Curva Grande → Roggia → Lesmo →
   Ascari → Parabolica; La Source → Eau Rouge/Raidillon → Kemmel → … → Bus Stop; Hell Corner → Mountain
   Straight → The Cutting → Skyline → The Dipper → Forrest's Elbow → Conrod → The Chase → Murray's),
   профилем высот и шириной. Скрипт сам замыкает контур, сглаживает переходы, проверяет самопересечения и
   масштабирует под официальную длину круга. Это **приближение**: последовательность поворотов,
   длины и перепады высот близки к реальным, но точной копией трассы это не является.
2. **`tools/trackgen/fetch_osm_track.py`** — точная геометрия из **OpenStreetMap** + высоты из **SRTM 30 м**.
   Запускайте на своём компьютере (нужен интернет):
   ```powershell
   cd tools\trackgen
   python fetch_osm_track.py Monza
   python fetch_osm_track.py Spa
   python fetch_osm_track.py MountPanorama --plot
   ```
   Скрипт сам находит в данных OSM замкнутый контур нужной длины (отбрасывая пит-лейн и старые
   конфигурации). Если результат не понравится — `python generate_tracks.py` вернёт приближённую версию.

## Модели автомобилей

Сейчас кузова **процедурные low-poly** (`CarBodyBuilder.cs`), построенные по реальным габаритам
(длина, ширина, высота, база, колея, свесы, компоновка «передний двигатель / средний двигатель»).
Это заглушки, а не детальные модели. Чтобы поставить настоящую модель:

1. Найдите модель (например, на Sketchfab) или сделайте её в Blender. Экспортируйте FBX:
   нос по оси +Z, начало координат на земле посередине между осями, масштаб 1 единица = 1 м, **без колёс**.
2. Положите модель в проект, создайте из неё prefab и сохраните как
   `Assets/Resources/CarModels/<id>.prefab`, где `<id>`: `AstonMartinVantageGT3`, `Ferrari296GT3`
   или `BMWM4GT3`.
3. Всё. Prefab подхватится вместо процедурного кузова; колёса и физика останутся прежними.

## Рекомендуемые пакеты и плагины Unity

В `Packages/manifest.json` подключено только необходимое (физика, Terrain, аудио, UI, интеграция с
VS Code/Rider), чтобы проект гарантированно открывался. По мере развития добавляйте через
*Window → Package Manager*:

| Пакет | Зачем |
|---|---|
| Universal RP (`com.unity.render-pipelines.universal`) | более красивая картинка; код материалов уже поддерживает URP |
| Input System (`com.unity.inputsystem`) | рули с педалями (Logitech, Thrustmaster, Fanatec) и force feedback |
| Cinemachine (`com.unity.cinemachine`) | продвинутые камеры и повторы |
| ML-Agents (`com.unity.ml-agents`) | обучение нейросетевых соперников — хорошая тема для изучения нейросетей |
| Unity MCP ([CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)) | управление редактором Unity из Claude Code (создание объектов, запуск, чтение логов) |

## Навыки Claude Code

В `.claude/skills/jev-skill-suggestion/` установлен мод **jev-skill-suggestion** (из
[claude-code-templates](https://www.aitmpl.com)): он убирает из контекста Claude длинный список навыков и
на каждый запрос сам подбирает не больше одного подходящего. Работает в Claude Code 2.1.287+ после того,
как вы откроете папку проекта в `claude` и подтвердите доверие к ней. Без API-ключа используется встроенный
классификатор Claude Code (ничего никуда не отправляется); с ключом TypeSafe или Vercel AI Gateway — модель Jev
(тогда текст запроса и описания навыков уходят этому сервису). Команда `/jev-skill-suggestion:setup` по желанию
скрывает навыки из списка совсем (`setup restore` — откат). Подробности — в README мода.

## Структура

```
RacingSim/                     проект Unity
  Assets/Scripts/Core/         запуск, сессия, меню/HUD, камеры, материалы
  Assets/Scripts/Vehicle/      физика машины: шины, подвеска, трансмиссия, аэро, звук, кузов
  Assets/Scripts/Track/        данные трассы, построение геометрии, хронометраж
  Assets/Resources/Cars/       характеристики машин (JSON)
  Assets/Resources/Tracks/     центральные линии трасс (JSON)
setup/install-windows.ps1      установка всего на диск F:
tools/trackgen/                генерация трасс (приближение и OpenStreetMap+SRTM)
tools/physics-test/            тест физики без Unity (dotnet run)
.claude/skills/                моды и навыки Claude Code для проекта
```

## Если что-то не работает

* **Ошибка `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class`** —
  Edit → Project Settings → Player → *Active Input Handling* → **Both** (или *Input Manager (Old)*).
* **Розовые материалы** — включён URP без URP-шейдеров в сборке; в редакторе код выбирает шейдер сам.
  Для сборки (.exe) добавьте `Standard` (или URP Lit) в *Project Settings → Graphics → Always Included Shaders*.
* **Машина «дёргается» или улетает** — проверьте, что ничего не меняет `Time.fixedDeltaTime` (должно быть 0.002).
* **Скрипт установки не нашёл версию Unity** — поставьте Unity 6 LTS вручную через Hub (Installs → Install Editor),
  папка редакторов уже будет указывать на F:.
