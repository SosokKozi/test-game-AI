---
name: input-ffb
description: "Управление в RacingSim - клавиатура, геймпад, игровые рули с педалями (Logitech G29/G923, Thrustmaster, Fanatec, Moza) через Unity Input System, калибровка, мёртвые зоны, линейность, отдельное сцепление и H-шифтер, а также обратная связь руля (force feedback) из момента самовыравнивания шин. Используй, когда нужно подключить руль, настроить кнопки, сделать FFB, переназначение управления или меню настроек управления. Keywords: input, Input System, steering wheel, pedals, force feedback, FFB, self-aligning torque, Logitech, Fanatec, Thrustmaster, gamepad, rebinding, deadzone."
---

# Управление и force feedback

Сейчас: `Vehicle/DriverInput.cs` — старый Input Manager (клавиатура + ось `Horizontal` геймпада). Это нормально для старта, но для рулей нужен Input System.

## Переход на Input System

1. Package Manager → **Input System**. Active Input Handling → **Both** (старый код продолжает работать).
2. Создать `Assets/Settings/RacingControls.inputactions`: карта `Driving` — `Steer` (Axis, −1..1), `Throttle`, `Brake`, `Clutch` (Axis 0..1), `ShiftUp`, `ShiftDown`, `Handbrake`, `Reset`, `Camera`, `Pause` + схемы Keyboard, Gamepad, Joystick (руль).
3. `DriverInput` оставить фасадом с тем же API (`Steer`, `Throttle`, …, `ConsumeShiftUp`) — физика не должна знать об источнике ввода. Источник выбирается автоматически по последнему активному устройству.
4. Рули видны как `Joystick`/HID: оси часто «перевёрнуты» (педаль отпущена = 1). Нужен экран калибровки: min/max каждой оси, инверсия, мёртвая зона, гамма (линейность), сохранение в `PlayerPrefs`/JSON по имени устройства.
5. Угол поворота руля: `steering.lockToLockDeg` машины (540° у GT3) против диапазона руля (900° у G29) — либо программное ограничение (soft lock), либо пересчёт `Steer = wheelAngle / (lockToLock/2)`.

## Force feedback

Источник силы — **момент самовыравнивания** передних шин (Mz):
- Модель: `Mz ≈ −t(α)·Fy`, пневматический след `t(α) = t0·(1 − |α|/α_peak)` до пика, дальше быстро к нулю — так водитель «чувствует» потерю сцепления.
- `t0` ≈ 0.03–0.05 м. Суммировать по FL/FR с учётом рычага рулевой трапеции, масштабировать к максимуму руля.
- Добавки: демпфирование (∝ скорости вращения руля), неровности/поребрики (`SurfaceInfo.bumpiness` → вибрация), удары, вибрация ABS.
- Частота: считать в FixedUpdate (500 Гц), отправлять на устройство с частотой ~100–400 Гц, ограничивая скачки.

Отправка FFB в Unity: Input System не даёт полноценного DirectInput FFB. Варианты:
- **Logitech Steering Wheel SDK** (G29/G923, Windows, бесплатный для разработки) — проще всего;
- обёртка над **DirectInput** через нативный плагин (C++ DLL) — для Fanatec/Thrustmaster/Moza;
- готовые ассеты из Asset Store (проверить лицензию).
Код FFB изолировать в `Vehicle/ForceFeedback/` с интерфейсом `IForceFeedbackDevice`, чтобы без руля всё работало.

## Проверка

- Без руля: клавиатура и геймпад работают как раньше.
- С рулём: калибровка сохраняется, педали 0..1, руль центрирован, FFB ослабевает при сносе передней оси и не «бьёт» на месте.
