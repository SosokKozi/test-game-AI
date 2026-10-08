<#
.SYNOPSIS
    Установка всего необходимого для RacingSim на диск F: (Windows 10/11).

.DESCRIPTION
    Ставит через winget и официальные установщики:
      - Git (+ Git LFS)                → F:\GameDev\Apps\Git
      - Unity Hub                      → F:\GameDev\Apps\UnityHub
      - Unity 6 Editor (последний 6000.x) → F:\GameDev\Unity\Editors
      - Visual Studio Code + расширения для C#/Unity/Python → F:\GameDev\Apps\VSCode
      - JetBrains Rider (по ключу -WithRider)              → F:\GameDev\Apps\Rider
      - .NET 8 SDK                     → F:\GameDev\Apps\dotnet
      - Python 3.12 + numpy/scipy/matplotlib/networkx/requests → F:\GameDev\Apps\Python312
      - Blender                        → F:\GameDev\Apps\Blender
    Переносит кэши (пакеты Unity, Asset Store, pip, NuGet, npm) на F:,
    клонирует проект в F:\GameDev\Projects\test-game-AI.

    Запуск (PowerShell от имени администратора):
        Set-ExecutionPolicy -Scope Process Bypass -Force
        .\install-windows.ps1
    Параметры:
        -Root F:\GameDev        корневая папка
        -UnityVersion 6000.0.xxfx  конкретная версия Unity (по умолчанию — последняя 6000.x из Hub)
        -WithRider              поставить JetBrains Rider (бесплатен для некоммерческого использования)
        -SkipUnity / -SkipBlender / -SkipVSCode / -SkipPython — пропустить компонент
#>
[CmdletBinding()]
param(
    [string]$Root = "F:\GameDev",
    [string]$UnityVersion = "",
    [string]$RepoUrl = "https://github.com/SosokKozi/test-game-AI.git",
    [string]$Branch = "",
    [switch]$WithRider,
    [switch]$SkipUnity,
    [switch]$SkipBlender,
    [switch]$SkipVSCode,
    [switch]$SkipPython
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Ok($msg) { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "  [!] $msg" -ForegroundColor Yellow }

# ---------------------------------------------------------------- проверки
$drive = Split-Path -Qualifier $Root
if (-not (Test-Path "$drive\")) { throw "Диск $drive не найден. Укажите другой путь: -Root X:\GameDev" }
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw "winget не найден. Установите 'App Installer' из Microsoft Store и перезапустите PowerShell."
}
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Warn "PowerShell запущен без прав администратора — часть установщиков может запросить подтверждение UAC." }

$Apps = Join-Path $Root "Apps"
$Editors = Join-Path $Root "Unity\Editors"
$Projects = Join-Path $Root "Projects"
$Cache = Join-Path $Root "Cache"
foreach ($d in @($Apps, $Editors, $Projects, "$Cache\upm", "$Cache\assetstore", "$Cache\pip", "$Cache\nuget", "$Cache\npm", "$Cache\temp")) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

function Set-UserEnv($name, $value) {
    [Environment]::SetEnvironmentVariable($name, $value, "User")
    Set-Item -Path "Env:$name" -Value $value
    Ok "$name = $value"
}

function Add-UserPath($dir) {
    $p = [Environment]::GetEnvironmentVariable("Path", "User")
    if (-not $p) { $p = "" }
    if (($p -split ";") -notcontains $dir) {
        [Environment]::SetEnvironmentVariable("Path", ($p.TrimEnd(";") + ";" + $dir).TrimStart(";"), "User")
    }
    if (($env:Path -split ";") -notcontains $dir) { $env:Path = "$env:Path;$dir" }
}

function Install-Winget($id, $location, [string]$override = "") {
    $installed = winget list --id $id -e --accept-source-agreements 2>$null | Select-String -SimpleMatch $id
    if ($installed) { Ok "$id уже установлен (пропуск; путь не меняется)"; return }
    Write-Host "  установка $id → $location"
    $wargs = @("install", "--id", $id, "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements")
    if ($override) { $wargs += @("--override", $override) } else { $wargs += @("--location", $location) }
    & winget @wargs
    if ($LASTEXITCODE -ne 0) { Warn "winget вернул код $LASTEXITCODE для $id — проверьте вывод выше" } else { Ok $id }
}

# ---------------------------------------------------------------- кэши на F:
Step "Перенос кэшей на $drive"
Set-UserEnv "UPM_CACHE_ROOT" "$Cache\upm"              # глобальный кэш пакетов Unity
Set-UserEnv "ASSETSTORE_CACHE_PATH" "$Cache\assetstore" # загрузки из Asset Store
Set-UserEnv "PIP_CACHE_DIR" "$Cache\pip"
Set-UserEnv "NUGET_PACKAGES" "$Cache\nuget"
Set-UserEnv "npm_config_cache" "$Cache\npm"

# ---------------------------------------------------------------- Git
Step "Git + Git LFS"
Install-Winget "Git.Git" "$Apps\Git"
Add-UserPath "$Apps\Git\cmd"
if (Get-Command git -ErrorAction SilentlyContinue) { git lfs install | Out-Null; Ok "git lfs install" }

# ---------------------------------------------------------------- .NET SDK
Step ".NET 8 SDK"
$dotnetDir = "$Apps\dotnet"
if (-not (Test-Path "$dotnetDir\dotnet.exe")) {
    $installer = "$Cache\temp\dotnet-install.ps1"
    Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
    & $installer -Channel 8.0 -InstallDir $dotnetDir
}
Set-UserEnv "DOTNET_ROOT" $dotnetDir
Add-UserPath $dotnetDir
Set-UserEnv "DOTNET_CLI_TELEMETRY_OPTOUT" "1"

# ---------------------------------------------------------------- Python
if (-not $SkipPython) {
    Step "Python 3.12 (инструменты генерации трасс)"
    $py = "$Apps\Python312"
    Install-Winget "Python.Python.3.12" $py "/quiet InstallAllUsers=0 TargetDir=`"$py`" PrependPath=0 Include_launcher=0 Include_test=0"
    Add-UserPath $py
    Add-UserPath "$py\Scripts"
    if (Test-Path "$py\python.exe") {
        & "$py\python.exe" -m pip install --upgrade pip
        & "$py\python.exe" -m pip install numpy scipy matplotlib networkx requests
        Ok "pip: numpy scipy matplotlib networkx requests"
    } else { Warn "python.exe не найден в $py — установите Python вручную в эту папку" }
}

# ---------------------------------------------------------------- Редакторы кода
if (-not $SkipVSCode) {
    Step "Visual Studio Code"
    Install-Winget "Microsoft.VisualStudioCode" "$Apps\VSCode"
    $code = "$Apps\VSCode\bin\code.cmd"
    if (Test-Path $code) {
        foreach ($ext in @("ms-dotnettools.csdevkit", "visualstudiotoolsforunity.vstuc", "ms-python.python")) {
            & $code --install-extension $ext --force | Out-Null
            Ok "расширение $ext"
        }
    } else { Warn "VS Code установлен не в $Apps\VSCode — расширения поставьте вручную: C# Dev Kit, Unity, Python" }
}
if ($WithRider) {
    Step "JetBrains Rider"
    Install-Winget "JetBrains.Rider" "$Apps\Rider"
}

# ---------------------------------------------------------------- Blender
if (-not $SkipBlender) {
    Step "Blender (3D-модели машин и окружения)"
    Install-Winget "BlenderFoundation.Blender" "$Apps\Blender"
}

# ---------------------------------------------------------------- Unity
if (-not $SkipUnity) {
    Step "Unity Hub"
    Install-Winget "Unity.UnityHub" "$Apps\UnityHub"
    $hub = @("$Apps\UnityHub\Unity Hub.exe", "$env:ProgramFiles\Unity Hub\Unity Hub.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $hub) {
        Warn "Unity Hub не найден. Установите его вручную (https://unity.com/download) в $Apps\UnityHub и перезапустите скрипт."
    } else {
        function Invoke-Hub([string[]]$hubArgs) {
            $out = "$Cache\temp\hub-out.txt"
            $p = Start-Process -FilePath $hub -ArgumentList (@("--", "--headless") + $hubArgs) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out
            $text = if (Test-Path $out) { Get-Content $out -Raw } else { "" }
            return @{ Code = $p.ExitCode; Text = $text }
        }

        Step "Unity Editor → $Editors"
        Invoke-Hub @("install-path", "--set", $Editors) | Out-Null
        Ok "Папка редакторов Unity Hub: $Editors"

        if (-not $UnityVersion) {
            $rel = Invoke-Hub @("editors", "--releases")
            $versions = [regex]::Matches($rel.Text, "\b6000\.\d+\.\d+f\d+\b") | ForEach-Object { $_.Value } | Sort-Object -Unique
            if ($versions) {
                $UnityVersion = $versions | Sort-Object { [version](($_ -replace "f", ".")) } | Select-Object -Last 1
            }
        }
        if (-not $UnityVersion) {
            Warn "Не удалось определить версию Unity 6 автоматически. Откройте Unity Hub → Installs → Install Editor → Unity 6 (LTS)."
        } else {
            $installed = Invoke-Hub @("editors", "--installed")
            if ($installed.Text -match [regex]::Escape($UnityVersion)) {
                Ok "Unity $UnityVersion уже установлен"
            } else {
                Write-Host "  установка Unity $UnityVersion (несколько ГБ, может занять 10–30 минут)…"
                $r = Invoke-Hub @("install", "--version", $UnityVersion)
                if ($r.Code -eq 0) { Ok "Unity $UnityVersion" } else { Warn "Hub вернул код $($r.Code). Вывод:`n$($r.Text)" }
            }
        }
        Warn "Unity Hub → Preferences → Locations: проверьте, что 'Downloads' тоже указывает на $drive (например $Cache\temp)."
        Warn "Войдите в Unity Hub и активируйте бесплатную лицензию Personal (Hub → Preferences → Licenses)."
    }
}

# ---------------------------------------------------------------- проект
Step "Проект RacingSim"
$proj = Join-Path $Projects "test-game-AI"
if (-not (Test-Path "$proj\.git")) {
    git clone $RepoUrl $proj
}
if ($Branch) { git -C $proj fetch origin $Branch; git -C $proj checkout $Branch }
git -C $proj lfs pull 2>$null
Ok "Проект: $proj"

Step "Готово"
Write-Host @"
Дальше:
  1. Откройте Unity Hub → Projects → Add → Add project from disk → $proj\RacingSim
     (выберите установленную версию Unity 6). Первый импорт займёт несколько минут.
  2. В Unity нажмите Play — появится меню выбора трассы и машины.
  3. Тест физики без Unity:  cd $proj\tools\physics-test ; dotnet run
  4. Перегенерировать трассы:  python $proj\tools\trackgen\generate_tracks.py --plot tracks.png
     Точная геометрия из OpenStreetMap: python $proj\tools\trackgen\fetch_osm_track.py Spa

Перезапустите терминал/проводник, чтобы новые переменные окружения (PATH, кэши) вступили в силу.
"@
