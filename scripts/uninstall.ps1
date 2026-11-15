# Убирает Explorer Panels: останавливает программу, отключает автозапуск, убирает команду из PATH и удаляет файлы.
# Серверы, запущенные из панелей, не останавливаются. Данные (разрешённые папки, состояние) остаются
# в %LOCALAPPDATA%\ExplorerPanels — удалите эту папку вручную, если они не нужны.

$ErrorActionPreference = "Stop"
$target = Join-Path $env:LOCALAPPDATA "Programs\ExplorerPanels"
if (-not (Test-Path $target)) { Write-Host "not installed"; return }

Get-Process explorer-panel-host -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -and $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) } |
  ForEach-Object { $_ | Stop-Process -Force; $_.WaitForExit(5000) | Out-Null }

$cli = Join-Path $target "explorer-panel.exe"
if (Test-Path $cli) { & $cli uninstall }
Remove-Item $target -Recurse -Force
Write-Host "uninstalled"
