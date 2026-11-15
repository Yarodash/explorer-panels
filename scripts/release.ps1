# Собирает готовую сборку Explorer Panels — архив, который ставится без .NET SDK и Node.js:
#   artifacts\ExplorerPanels-<версия>-win-x64.zip
# В архиве: программа вместе со средой выполнения .NET, страница панели, install.cmd и uninstall.cmd.
#
#   scripts\release.ps1            собрать архив и проверить его
#   scripts\release.ps1 -Publish   то же и выложить выпуск v<версия> на GitHub (нужен gh; ветка должна быть запушена)
#
# Версия берётся из Directory.Build.props.

param([switch]$Publish)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $repo "Directory.Build.props"))).Project.PropertyGroup.Version
if (-not $version) { throw "no <Version> in Directory.Build.props" }
$name = "ExplorerPanels-$version-win-x64"
$out = Join-Path $repo "artifacts\release\ExplorerPanels"
$zip = Join-Path $repo "artifacts\$name.zip"

function Step($title, [scriptblock]$action) {
  Write-Host "== $title"
  & $action
  if ($LASTEXITCODE -ne 0) { throw "$title failed (exit code $LASTEXITCODE)" }
}

# ───────── сборка ─────────
Push-Location (Join-Path $repo "src\ui")
try {
  if (-not (Test-Path node_modules)) { Step "npm ci" { npm ci --no-audit --no-fund } }
  Step "build the panel page" { npm run build }
}
finally { Pop-Location }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
# --self-contained: среда выполнения .NET лежит рядом с программой, ставить её отдельно не нужно
$publishArgs = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:DebugType=none", "-o", $out, "--nologo", "-v", "q")
Step "publish the host" { dotnet publish (Join-Path $repo "src\host") @publishArgs }
Step "publish the command" { dotnet publish (Join-Path $repo "src\cli") @publishArgs }
Copy-Item (Join-Path $repo "src\ui\dist") (Join-Path $out "ui") -Recurse
Copy-Item (Join-Path $PSScriptRoot "install.ps1"), (Join-Path $PSScriptRoot "uninstall.ps1") $out
Copy-Item (Join-Path $repo "LICENSE") $out -ErrorAction SilentlyContinue

$launcher = "@echo off`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0{0}.ps1`"`r`nif errorlevel 1 pause`r`n"
[IO.File]::WriteAllText((Join-Path $out "install.cmd"), ($launcher -f "install"))
[IO.File]::WriteAllText((Join-Path $out "uninstall.cmd"), ($launcher -f "uninstall"))
[IO.File]::WriteAllText((Join-Path $out "README.txt"), @"
Explorer Panels $version
https://github.com/Yarodash/explorer-panels

Install:    run install.cmd  (copies the program to %LOCALAPPDATA%\Programs\ExplorerPanels,
            starts it at sign-in and puts the explorer-panel command on PATH)
Update:     run install.cmd of a newer version
Uninstall:  run uninstall.cmd

Needs Windows 11. Nothing else has to be installed.
"@)

if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "== zip"
Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("built: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# ───────── проверка архива: распаковать и убедиться, что программа из него работает ─────────
Write-Host "== check the archive"
$work = Join-Path $env:TEMP ("explorer-panel-release-" + [Guid]::NewGuid().ToString("N").Substring(0, 6))
$project = Join-Path $work "project"
New-Item -ItemType Directory (Join-Path $project ".explorer_components") -Force | Out-Null
Expand-Archive $zip (Join-Path $work "unzipped")
$app = Join-Path $work "unzipped\ExplorerPanels"
[IO.File]::WriteAllText((Join-Path $project ".explorer_components\panel.json"), '{ "actions": { "mark": { "run": "echo ok> marker.txt" } } }')

$problems = @()
foreach ($file in "explorer-panel.exe", "explorer-panel-host.exe", "ui\index.html", "install.cmd", "uninstall.cmd", "coreclr.dll", "WebView2Loader.dll") {
  if (-not (Test-Path (Join-Path $app $file))) { $problems += "missing in the archive: $file" }
}
$env:EXPLORER_PANEL_DATA = Join-Path $work "data"      # своя папка данных: установленная программа не затрагивается
$env:EXPLORER_PANEL_ONLY_UNDER = $work                  # и окна Проводника пользователя — тоже
$hostProcess = $null
try {
  $cli = Join-Path $app "explorer-panel.exe"
  $reported = (& $cli version) -join " "
  if ($reported -notmatch [regex]::Escape($version)) { $problems += "version: expected $version, got '$reported'" }
  & $cli trust $project | Out-Null
  $hostProcess = Start-Process (Join-Path $app "explorer-panel-host.exe") -PassThru
  $ready = $false
  for ($i = 0; $i -lt 40 -and -not $ready; $i++) { Start-Sleep -Milliseconds 250; $ready = ((& $cli check $project) -join " ") -match "host: running" }
  if (-not $ready) { $problems += "the program from the archive did not start" }
  else {
    & $cli do mark $project | Out-Null
    if (-not (Test-Path (Join-Path $project "marker.txt"))) { $problems += "an action did not run through the program from the archive" }
  }
}
finally {
  if ($hostProcess -and -not $hostProcess.HasExited) { $hostProcess | Stop-Process -Force }
  Remove-Item Env:EXPLORER_PANEL_DATA, Env:EXPLORER_PANEL_ONLY_UNDER -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "PROBLEM: $_" }; throw "the archive is not usable" }
Write-Host "archive ok: the program starts from it and runs an action"

# ───────── выпуск на GitHub ─────────
if ($Publish) {
  $notes = @"
Ready-to-run build for Windows 11 (x64). Nothing else has to be installed.

1. Download **$name.zip** and unzip it anywhere.
2. Run **install.cmd**.

Windows may ask for confirmation because the build is not signed.
To update, run ``install.cmd`` of a newer version; ``uninstall.cmd`` removes the program.
"@
  Step "publish the release v$version" { gh release create "v$version" $zip --title "Explorer Panels $version" --notes $notes --target main }
}
