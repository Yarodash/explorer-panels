# Собирает программу целиком в artifacts\app:
#   explorer-panel-host.exe  — фоновая программа
#   explorer-panel.exe       — команда для агентов и скриптов
#   ui\                      — страница панели
#
# Нужны .NET SDK 10 и Node.js 20+.

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo "artifacts\app"

function Step($name, [scriptblock]$action) {
  Write-Host "== $name"
  & $action
  if ($LASTEXITCODE -ne 0) { throw "$name failed (exit code $LASTEXITCODE)" }
}

Push-Location (Join-Path $repo "src\ui")
try {
  if (-not (Test-Path node_modules)) { Step "npm ci" { npm ci --no-audit --no-fund } }
  Step "build the panel page" { npm run build }
}
finally { Pop-Location }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Step "publish the host" { dotnet publish (Join-Path $repo "src\host") -c Release -o $out --nologo -v q }
Step "publish the command" { dotnet publish (Join-Path $repo "src\cli") -c Release -o $out --nologo -v q }
Copy-Item (Join-Path $repo "src\ui\dist") (Join-Path $out "ui") -Recurse

Write-Host "built: $out"
