# Запускает проверочный экземпляр фоновой программы: со своей папкой данных и только для одного окна Проводника.
# Использование (из сценариев):  . .\lib.ps1;  $ctx = & .\start-host.ps1 -Path <папка проекта> [-Trust]
param([Parameter(Mandatory)][string]$Path, [switch]$Trust, [string]$Configuration = "Release")

. (Join-Path $PSScriptRoot "lib.ps1")

$data = Join-Path $env:TEMP ("explorer-panel-e2e-" + [Guid]::NewGuid().ToString("N").Substring(0, 6))
New-Item -ItemType Directory $data -Force | Out-Null

$hostExe = Join-Path $Repo "src\host\bin\$Configuration\net10.0-windows\explorer-panel-host.exe"
$cliExe = Join-Path $Repo "src\cli\bin\$Configuration\net10.0-windows\explorer-panel.exe"
$ui = Join-Path $Repo "src\ui\dist"
if (-not (Test-Path $hostExe)) { throw "build the host first: dotnet build src/host -c $Configuration" }
if (-not (Test-Path (Join-Path $ui "index.html"))) { throw "build the page first: npm --prefix src/ui run build" }

$explorer = New-TestExplorer $Path

# разрешение папки — тем же файлом, которым пользуется программа
if ($Trust) {
  $trustFile = Join-Path $data "trust.json"
  @{ trusted = @((Resolve-Path $Path).Path.TrimEnd('\')); blocked = @() } | ConvertTo-Json | Set-Content $trustFile -Encoding UTF8
}

$env:EXPLORER_PANEL_DATA = $data
$env:EXPLORER_PANEL_DEBUG = "1"
$env:EXPLORER_PANEL_UI = $ui
$env:EXPLORER_PANEL_ONLY = [int64]$explorer
$process = Start-Process $hostExe -PassThru

[pscustomobject]@{ Explorer = $explorer; Data = $data; Host = $process; Cli = $cliExe }
