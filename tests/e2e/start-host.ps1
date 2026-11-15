# Запускает проверочный экземпляр фоновой программы: со своей папкой данных и только для окон Проводника,
# открытых внутри папки $Under. Возвращает объект с путями и процессом.
#
#   . .\lib.ps1
#   $ctx = & .\start-host.ps1 -Under <папка> [-Trust <папка проекта>, ...]
param([Parameter(Mandatory)][string]$Under, [string[]]$Trust = @(), [string]$Configuration = "Release")

. (Join-Path $PSScriptRoot "lib.ps1")

$data = Join-Path $env:TEMP ("explorer-panel-e2e-data-" + [Guid]::NewGuid().ToString("N").Substring(0, 6))
New-Item -ItemType Directory $data -Force | Out-Null

$hostExe = Join-Path $Repo "src\host\bin\$Configuration\net10.0-windows\explorer-panel-host.exe"
$cliExe = Join-Path $Repo "src\cli\bin\$Configuration\net10.0-windows\explorer-panel.exe"
$ui = Join-Path $Repo "src\ui\dist"
if (-not (Test-Path $hostExe)) { throw "build the host first: dotnet build src/host -c $Configuration" }
if (-not (Test-Path $cliExe)) { throw "build the command first: dotnet build src/cli -c $Configuration" }
if (-not (Test-Path (Join-Path $ui "index.html"))) { throw "build the page first: npm --prefix src/ui run build" }

# разрешённые папки — тем же файлом, которым пользуется программа
if ($Trust.Count -gt 0) {
  $trusted = @($Trust | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') })
  [pscustomobject]@{ trusted = $trusted; blocked = @() } | ConvertTo-Json | Set-Content (Join-Path $data "trust.json") -Encoding UTF8
}

# Кнопки Claude поначалу выключены: так проверяется, что без них в папке без panel.json панели нет.
# Их включает отдельный раздел прогона — так же, как галочка в трее: через файл настроек.
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "claudeButtons": false }')

$env:EXPLORER_PANEL_DATA = $data
$env:EXPLORER_PANEL_CLAUDE_DRY = "1"   # нажатие кнопки Claude только записывается в журнал: окно терминала не открывается
$env:EXPLORER_PANEL_DEBUG = "1"
$env:EXPLORER_PANEL_UI = $ui
$env:EXPLORER_PANEL_ONLY_UNDER = [IO.Path]::GetFullPath($Under)
$process = Start-Process $hostExe -PassThru

[pscustomobject]@{ Data = $data; Host = $process; Cli = $cliExe; HostExe = $hostExe }
