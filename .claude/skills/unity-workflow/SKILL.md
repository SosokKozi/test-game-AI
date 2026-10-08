---
name: unity-workflow
description: "Как работать с проектом RacingSim в Unity на компьютере пользователя - открытие проекта, запуск без сцен (GameBootstrap), управление редактором через Unity MCP и официальный плагин Unity, чтение консоли и Editor.log, типичные ошибки (пустой проект, Input, розовые материалы, версия Unity), совместимость API, сборка .exe, коммиты на GitHub, диск F:. Используй в начале каждой сессии, при ошибках Unity, при запуске/проверке игры и перед коммитом. Keywords: Unity Editor, MCP, Play mode, console, Editor.log, compile errors, build, git commit, push, Windows, PowerShell."
---

# Работа с Unity и репозиторием

Пользователь — новичок, общается по-русски. Объясняй коротко и по шагам, перед командами — одна фраза, что команда делает.

## Устройство проекта

- Проект Unity — папка `RacingSim/` (её открывают в Unity Hub, **не** корень репозитория).
- Сцен нет: `Core/GameBootstrap.cs` при Play в любой сцене создаёт `RaceSession` → меню (IMGUI) → трасса и машина строятся в рантайме из `Resources`. **Пустая сцена до Play — это нормально.**
- Пакеты: только минимум (`Packages/manifest.json`, `com.unity.ugui 2.0.0` = Unity 6). Для Unity 2022 — `com.unity.ugui 1.0.0`.

## Начало сессии (с Unity MCP)

1. Проверь подключение: прочитать консоль редактора, версию Unity, открытый проект (должен быть `.../RacingSim`).
2. Есть красные ошибки компиляции → сначала они. Код не исполняется, пока есть хоть одна.
3. Play → меню → «СТАРТ» → проверь, что появились трасса, машина, HUD; телеметрия — `H`.
4. Без MCP: лог редактора `%LOCALAPPDATA%\Unity\Editor\Editor.log` (Windows).

## Типичные проблемы

| Проблема | Решение |
|---|---|
| Assets пустой | В Hub добавлена не та папка — нужна `RacingSim` |
| Ничего не происходит по Play | Ошибки в Console; проверь, что `GameBootstrap` скомпилирован |
| `InvalidOperationException ... UnityEngine.Input` | Project Settings → Player → Active Input Handling → **Both** |
| Розовые материалы | URP без нужного шейдера; `MaterialFactory` выбирает `Standard`/URP Lit сам; для билда — Always Included Shaders |
| Ошибка пакетов при открытии | Версия Unity ≠ 6 → поправить версии в `manifest.json` |
| Физика «взрывается» | `Time.fixedDeltaTime` должен быть 0.002 (задаёт `RaceSession`) |

## Правила кода

- Комментарии и тексты UI — на русском.
- API совместим с Unity 2021.3+ и Unity 6: без `Rigidbody.velocity/linearVelocity`, `PhysicMaterial/PhysicsMaterial`, `FindObjectOfType` (используй `FindAnyObjectByType`).
- Не превращать рантайм-построение в сцены/префабы без согласования: всё проверяемое должно жить в C# и JSON.
- Каждый новый `MonoBehaviour`/скрипт — в свою папку по смыслу (`Core`, `Vehicle`, `Track`, при росте — `AI`, `UI`, `Race`, `Audio`).
- После правок физики — `tools/physics-test` (`dotnet run`), после правок трасс — `generate_tracks.py --plot`.

## Сборка игры (.exe)

File → Build Profiles → Windows → добавить открытую сцену (Untitled → сохранить как `Assets/Scenes/Main.unity`) → Build в `F:\GameDev\Builds\RacingSim`. Проверить, что шейдеры включены (см. таблицу).

## Git

- Ветка разработки: `claude/adoring-albattani-93trad` (пока пользователь не попросит другую).
- Коммит — только по просьбе пользователя («закоммить», «отправь на GitHub»). Сообщение — что и зачем, по-русски или по-английски.
- Перед коммитом: `git status`, убедиться, что нет `Library/`, `Temp/`, `Logs/`, `UserSettings/` (они в `.gitignore`), большие бинарники идут через LFS.
- Работа идёт на диске F: — не создавать кэши и временные файлы на C: без необходимости.
