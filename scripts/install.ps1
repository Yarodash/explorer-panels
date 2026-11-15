# Ставит Explorer Panels для текущего пользователя:
#   собирает программу, кладёт её в %LOCALAPPDATA%\Programs\ExplorerPanels,
#   включает автозапуск при входе в систему, добавляет команду explorer-panel в PATH и запускает программу.
#
# Повторный запуск обновляет программу. Серверы, запущенные из панелей, при этом продолжают работать.
#
#   scripts\install.ps1            собрать и поставить
#   scripts\install.ps1 -NoBuild   поставить уже собранное (artifacts\app)
#
# Этот же файл лежит в готовой сборке (архив из scripts\release.ps1): там он ставит программу из своей папки,
# ничего не собирая, — .NET SDK и Node.js для этого не нужны.

param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$target = Join-Path $env:LOCALAPPDATA "Programs\ExplorerPanels"

if (Test-Path (Join-Path $PSScriptRoot "explorer-panel-host.exe")) {
  # запущен из готовой сборки: программа лежит рядом
  $source = $PSScriptRoot
}
else {
  $repo = Split-Path $PSScriptRoot -Parent
  $source = Join-Path $repo "artifacts\app"
  if (-not $NoBuild) { & (Join-Path $PSScriptRoot "build.ps1") }
  if (-not (Test-Path (Join-Path $source "explorer-panel-host.exe"))) { throw "nothing to install: run scripts\build.ps1 first" }
}

# останавливаем только установленный экземпляр (проверочные экземпляры из папки сборки не трогаем)
$running = Get-Process explorer-panel-host -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
  Write-Host "== stopping the running program"
  $running | Stop-Process -Force
  $running | ForEach-Object { $_.WaitForExit(5000) | Out-Null }
}

if ([IO.Path]::GetFullPath($source).TrimEnd('\') -ne [IO.Path]::GetFullPath($target).TrimEnd('\')) {
  Write-Host "== copying to $target"
  New-Item -ItemType Directory -Force $target | Out-Null
  robocopy $source $target /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "copy failed (robocopy exit code $LASTEXITCODE)" }
}

Write-Host "== registering"
& (Join-Path $target "explorer-panel.exe") install
if ($LASTEXITCODE -ne 0) { throw "registration failed" }

Write-Host "== starting"
Start-Process (Join-Path $target "explorer-panel-host.exe")
Write-Host "installed. A tray icon appears; panels show up in File Explorer in folders that have .explorer_components\panel.json"
