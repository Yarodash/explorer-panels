# Сквозные проверки: настоящая фоновая программа + настоящий Проводник на отдельном рабочем столе "Claude tests".
#
#   powershell -ExecutionPolicy Bypass -File tests\e2e\run.ps1
#
# Что делает прогон:
#   * на время останавливает установленную программу (иначе в тестовом окне было бы две панели) и в конце запускает снова;
#   * создаёт временный проект в папке с пробелами и кириллицей, сервер — python -m http.server;
#   * не трогает окна на рабочем столе пользователя, не активирует окна, не имитирует мышь и клавиши.
# Нужны: собранные src/host, src/cli (Release) и src/ui/dist, Python, модуль PowerShell VirtualDesktop.

. (Join-Path $PSScriptRoot "lib.ps1")

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail = "") {
  $results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
  Write-Host ("{0}  {1}{2}" -f $(if ($Ok) { "PASS" } else { "FAIL" }), $Name, $(if ($Detail) { "  — $Detail" } else { "" }))
}

# свободный порт для тестового сервера
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0); $listener.Start()
$port = $listener.LocalEndpoint.Port; $listener.Stop()

# ───────── временный проект ─────────
$base = Join-Path $env:TEMP ("explorer-panel-e2e-" + [Guid]::NewGuid().ToString("N").Substring(0, 6))
$project = Join-Path $base "проект с пробелами"
$plain = Join-Path $base "plain"
New-Item -ItemType Directory (Join-Path $project ".explorer_components"), (Join-Path $project "sub"), $plain -Force | Out-Null
$panelJson = @"
{
  "services": { "api": { "run": "python -m http.server $port --bind 127.0.0.1", "health": "http://127.0.0.1:$port/" } },
  "actions": { "mark": { "run": "echo marked> marker.txt" }, "envdump": { "run": "set > env.txt" } },
  "values": { "note": {} },
  "widgets": [
    { "type": "clock" }, { "type": "stopwatch" }, { "type": "separator" },
    { "type": "text", "text": "note: {note}" },
    { "type": "select", "value": "mode", "options": ["dev", "test", "prod"] },
    { "type": "separator" },
    { "type": "status", "service": "api" },
    { "type": "button", "icon": "play", "tooltip": "Start", "do": "start", "pin": true },
    { "type": "button", "icon": "square", "tooltip": "Stop", "do": "stop", "pin": true },
    { "type": "button", "icon": "file-text", "tooltip": "Logs", "do": "logs" },
    { "type": "menu", "icon": "ellipsis", "tooltip": "More", "pin": true, "items": [ { "label": "Mark", "do": "mark" } ] }
  ]
}
"@
[IO.File]::WriteAllText((Join-Path $project ".explorer_components\panel.json"), $panelJson)

$installed = Join-Path $env:LOCALAPPDATA "Programs\ExplorerPanels\explorer-panel-host.exe"
$wasRunning = [bool](Get-Process explorer-panel-host -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installed })
if ($wasRunning) { Get-Process explorer-panel-host | Where-Object { $_.Path -eq $installed } | Stop-Process -Force; Start-Sleep -Milliseconds 800 }

$ctx = $null; $explorer = [IntPtr]::Zero
try {
  # программа запускается раньше, чем открыто окно Проводника: она должна найти окно, открытое позже
  $env:EP_E2E_INHERITED = "from the launching terminal" # программа не должна передавать это тому, что запускает
  $ctx = & (Join-Path $PSScriptRoot "start-host.ps1") -Under $base -Trust $project, $plain
  $data = $ctx.Data
  Start-Sleep -Seconds 2
  $explorer = New-TestExplorer $project

  function Page([string]$Script) { Invoke-PanelJs $explorer $Script $data }
  function ApiStatus { Page "document.querySelector('[data-panel] [data-service=api]')?.dataset.status ?? 'none'" }
  function Listening { try { $c = [System.Net.Sockets.TcpClient]::new(); $c.Connect("127.0.0.1", $port); $c.Close(); $true } catch { $false } }
  function Gap { $p = Get-Panel $explorer; $f = $Win::Frame($explorer); $r = $Win::Rect($p); "{0},{1}" -f ($f.R - $r.R), ($f.B - $r.B) }

  # 1. появление
  Check "panel appears in a window opened after the host started" (Wait-Until { Test-PanelVisible $explorer } 20000)
  $panel = Get-Panel $explorer
  Check "panel is on the test desktop, not the user's" ($Win::OnOtherDesktop($panel))

  # 2. окно панели: не отдельное окно, сразу над Проводником
  $ex = $Win::GetWindowLongPtr($panel, -20).ToInt64()
  Check "panel is not a separate window in the taskbar or Alt+Tab" ((($ex -band 0x40000) -eq 0) -and (($ex -band 0x80) -ne 0) -and (($ex -band 0x8000000) -ne 0))
  Check "panel sits right above its Explorer in the window stack" ($Win::GetWindow($explorer, 3) -eq $panel)

  # 3. клики мимо панели проходят: область окна — только панель
  [void](Wait-Until { $Win::RegionRects($panel).Count -gt 0 })
  $canvas = $Win::Rect($panel); $parts = $Win::RegionRects($panel)
  $solid = ($parts | ForEach-Object { $_.W * $_.H } | Measure-Object -Sum).Sum
  Check "only the panel itself receives clicks" ($solid -gt 0 -and $solid -lt 0.35 * $canvas.W * $canvas.H) ("solid {0} px² of canvas {1} px²" -f $solid, ($canvas.W * $canvas.H))

  # 4. следование за окном
  $gap = Gap; $before = $Win::Rect($panel)
  Move-TestExplorer $explorer 130 70
  Check "panel follows a move" (Wait-Until { $r = $Win::Rect((Get-Panel $explorer)); ($r.L - $before.L) -eq 130 -and ($r.T - $before.T) -eq 70 } 4000)
  Resize-TestExplorer $explorer -120 -90
  Check "panel keeps its corner after a resize" (Wait-Until { (Gap) -eq $gap } 4000) ("gap " + (Gap) + " vs " + $gap)

  # 5. сворачивание
  Hide-TestExplorer $explorer
  Check "panel hides when Explorer is minimized" (Wait-Until { -not $Win::IsWindowVisible((Get-Panel $explorer)) } 4000)
  Show-TestExplorer $explorer
  Check "panel returns when Explorer is restored" (Wait-Until { Test-PanelVisible $explorer } 6000)

  # 6. папки
  Set-TestExplorerFolder $explorer (Join-Path $project "sub")
  Start-Sleep -Milliseconds 1500
  Check "panel stays in a subfolder of the project" (Wait-Until { Test-PanelVisible $explorer } 5000)
  Set-TestExplorerFolder $explorer $plain
  Check "panel disappears in a folder without panel.json" (Wait-Until { -not (Test-PanelVisible $explorer) } 6000)

  # 7. автоподхват
  New-Item -ItemType Directory (Join-Path $plain ".explorer_components") -Force | Out-Null
  [IO.File]::WriteAllText((Join-Path $plain ".explorer_components\panel.json"), '{ "widgets": [ { "type": "text", "text": "half')
  Start-Sleep -Milliseconds 2500
  Check "a broken panel.json does not show a panel" (-not (Test-PanelVisible $explorer))
  $watch = [Diagnostics.Stopwatch]::StartNew()
  [IO.File]::WriteAllText((Join-Path $plain ".explorer_components\panel.json"), '{ "widgets": [ { "type": "text", "text": "picked up" } ] }')
  $picked = Wait-Until { Test-PanelVisible $explorer } 6000
  Check "a valid panel.json is picked up by itself" $picked ("after {0} ms" -f $watch.ElapsedMilliseconds)
  Check "the picked-up panel shows its widgets" ((Page "document.querySelector('[data-panel]')?.textContent ?? ''") -match "picked up")
  Remove-Item (Join-Path $plain ".explorer_components") -Recurse -Force
  Check "panel disappears when panel.json is deleted" (Wait-Until { -not (Test-PanelVisible $explorer) } 6000)

  # 8. серверы
  Set-TestExplorerFolder $explorer $project
  [void](Wait-Until { Test-PanelVisible $explorer } 6000)
  [void](Wait-Until { (ApiStatus) -eq "stopped" } 6000)
  Check "service is shown as stopped at first" ((ApiStatus) -eq "stopped") (ApiStatus)
  [void](Invoke-PanelClick $explorer '[data-action=start]' $data)
  Check "Start: service becomes running" (Wait-Until { (ApiStatus) -eq "running" } 30000) (ApiStatus)
  Check "Start: the server really listens" (Listening)
  Check "log file is written inside the project" (Test-Path (Join-Path $project ".explorer_components\logs\services.log"))
  Check "logs are git-ignored" ((Get-Content (Join-Path $project ".explorer_components\.gitignore") -Raw -ErrorAction SilentlyContinue) -match "logs/")

  # 9. падение
  Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
  Check "a crashed server becomes failed" (Wait-Until { (ApiStatus) -eq "failed" } 15000) (ApiStatus)
  [void](Invoke-PanelClick $explorer '[data-action=start]' $data)
  Check "Start again recovers it" (Wait-Until { (ApiStatus) -eq "running" } 30000) (ApiStatus)
  # 9b. серверы переживают закрытие программы (обновление, выход из трея, сбой)
  $ctx.Host | Stop-Process -Force
  Start-Sleep -Milliseconds 1500
  function Answers { try { (Invoke-WebRequest "http://127.0.0.1:$port/" -TimeoutSec 4 -UseBasicParsing).StatusCode -eq 200 } catch { $false } }
  Check "a server keeps answering after the host is killed" ((Answers) -and (Answers) -and (Answers))
  $ctx.Host = Start-Process $ctx.HostExe -PassThru
  Check "the restarted host shows the panel again" (Wait-Until { Test-PanelVisible $explorer } 20000)
  Check "the restarted host finds the running server" (Wait-Until { (ApiStatus) -eq "running" } 20000) (ApiStatus)

  [void](Invoke-PanelClick $explorer '[data-action=stop]' $data)
  Check "Stop: service becomes stopped" (Wait-Until { (ApiStatus) -eq "stopped" } 20000) (ApiStatus)
  Check "Stop: the server no longer listens" (Wait-Until { -not (Listening) } 5000)

  # 9a. меню открывается по нажатию указателя — проверяем настоящим событием мыши, а не программным click()
  [void](Invoke-PanelMouse $explorer '[data-panel] button[aria-label=More]' $data)
  Start-Sleep -Milliseconds 700
  Check "a menu opened by a real mouse press stays open" ((Page "!!document.querySelector('[role=menu]')") -eq $true)
  Check "the open menu is clickable (it is inside the window region)" ($Win::RegionRects((Get-Panel $explorer)).Count -gt 1 -or ($Win::RegionRects((Get-Panel $explorer)) | ForEach-Object { $_.H } | Measure-Object -Maximum).Maximum -gt 100)
  [void](Invoke-PanelMouse $explorer '[role=menuitem]' $data)
  Check "choosing a menu item runs its action" (Wait-Until { Test-Path (Join-Path $project "marker.txt") } 8000)
  Check "the menu closes after a choice" (Wait-Until { (Page "!!document.querySelector('[role=menu]')") -eq $false } 4000)

  # 10. команда explorer-panel и мгновенное обновление значения снаружи
  $env:EXPLORER_PANEL_DATA = $data
  $checkOutput = (& $ctx.Cli check $project) -join "`n"
  Check "explorer-panel check: OK, host running, trusted" ($LASTEXITCODE -eq 0 -and $checkOutput -match "OK:" -and $checkOutput -match "host: running" -and $checkOutput -match "trusted: yes") ($checkOutput -replace "`n", " | ")
  $watch.Restart()
  & $ctx.Cli set note=привет $project | Out-Null
  $shown = Wait-Until { (Page "document.querySelector('[data-panel]')?.textContent ?? ''") -match "note: привет" } 5000
  Check "explorer-panel set updates the panel at once" $shown ("after {0} ms" -f $watch.ElapsedMilliseconds)

  # 10b. окружение: команды панели получают обычное окружение пользователя, а не окружение запустившего терминала
  & $ctx.Cli do envdump $project | Out-Null
  $seen = @(Get-Content (Join-Path $project "env.txt") -ErrorAction SilentlyContinue | ForEach-Object { ($_ -split "=", 2)[0] })
  Check "a panel command does not inherit the launching terminal's variables" (($seen.Count -gt 10) -and ($seen -notcontains "EP_E2E_INHERITED") -and ($seen -notcontains "CLAUDE_CODE_CHILD_SESSION")) ("variables: " + $seen.Count)
  Check "a panel command still has the user's usual variables" (($seen -contains "USERPROFILE") -and (@($seen | Where-Object { $_ -ieq "Path" }).Count -eq 1))

  # 10c. снимок панели в файл: его делает сама страница, а не экран
  function IsPng([string]$File) { (Test-Path $File) -and ((Get-Item $File).Length -gt 500) -and (([IO.File]::ReadAllBytes($File)[1..3] -join ",") -eq "80,78,71") }
  $panelShot = Join-Path $base "panel shot.png"
  $shotOutput = (& $ctx.Cli screenshot $project --out $panelShot) -join "`n"
  Check "explorer-panel screenshot saves a picture of the panel" (($LASTEXITCODE -eq 0) -and (IsPng $panelShot)) $shotOutput
  & $ctx.Cli screenshot $project | Out-Null
  Check "without a file name the picture goes next to the project's logs" (IsPng (Join-Path $project ".explorer_components\logs\panel.png"))
  $noWindow = (& $ctx.Cli screenshot $plain 2>&1 | ForEach-Object { "$_" }) -join " "
  Check "a folder that is not open in File Explorer gets an explanation, not a picture" (($LASTEXITCODE -eq 1) -and ($noWindow -match "open the folder in File Explorer")) $noWindow
  Write-Host "panel shot: $panelShot"

  # 11. не помещается -> кнопка расширения
  $wide = $Win::Rect($explorer).W
  Resize-TestExplorer $explorer (-($wide - 620)) 0
  Check "a narrow window shows the widen button" (Wait-Until { (Page "!!document.querySelector('[data-fit-button]')") -eq $true } 6000)
  Check "pinned widgets survive in a narrow window" ((Page "document.querySelector('[data-panel] [data-action=start]')?.closest('[data-hidden]') === null") -eq $true)
  Check "less important widgets are hidden whole in a narrow window" ((Page "document.querySelectorAll('[data-panel] > [data-hidden]').length > 0") -eq $true)
  $narrow = $Win::Rect($explorer).W
  [void](Invoke-PanelClick $explorer '[data-fit-button]' $data)
  Check "the widen button makes everything fit" (Wait-Until { (Page "!!document.querySelector('[data-fit-button]')") -eq $false } 6000) ("explorer width {0} -> {1}" -f $narrow, $Win::Rect($explorer).W)

  # 12. кнопки Claude: есть в любой папке, пока включены (в трее); до этого места они были выключены
  $ctx.Host | Stop-Process -Force
  Start-Sleep -Milliseconds 800
  [IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "claudeButtons": true }')
  $ctx.Host = Start-Process $ctx.HostExe -PassThru
  $hostLog = Join-Path $data "host.log"
  function Logged([string]$Text) { ([string](Get-Content $hostLog -Raw -ErrorAction SilentlyContinue)).Contains($Text) }
  function ClaudeCount { Page "document.querySelectorAll('[data-panel] [data-claude] button').length" }

  Set-TestExplorerFolder $explorer $plain
  Check "Claude buttons appear in a folder without panel.json" (Wait-Until { (Test-PanelVisible $explorer) -and ((ClaudeCount) -eq 2) } 20000) ("buttons: " + (ClaudeCount))
  Check "such a folder shows nothing but the two buttons" ((Page "document.querySelectorAll('[data-panel] button').length") -eq 2)
  [void](Invoke-PanelMouse $explorer '[data-claude] button:nth-of-type(1)' $data)
  Check "the left button opens Claude in the folder shown" (Wait-Until { Logged "claude: opening in $plain" } 5000)
  [void](Invoke-PanelMouse $explorer '[data-claude] button:nth-of-type(2)' $data)
  Check "the right button continues the last conversation there" (Wait-Until { Logged "claude --continue: opening in $plain" } 5000)
  Save-Shot $explorer (Join-Path $base "claude-only.png") | Out-Null

  $sub = Join-Path $project "sub"
  Set-TestExplorerFolder $explorer $sub
  $atTheEnd = "(() => { const b = [...document.querySelectorAll('[data-panel] button')]; return b.length > 2 && b.slice(-2).every(x => !!x.closest('[data-claude]')) && !!document.querySelector('[data-panel] [data-action=start]') })()"
  Check "a project's panel gets the Claude buttons at its right end" (Wait-Until { (Page $atTheEnd) -eq $true } 8000)
  [void](Invoke-PanelMouse $explorer '[data-claude] button:nth-of-type(1)' $data)
  Check "in a project Claude opens in the folder shown, not in the project root" (Wait-Until { Logged "claude: opening in $sub" } 5000)

  # снимок для глаз
  $shot = Join-Path $base "final.png"
  Save-Shot $explorer $shot | Out-Null
  Write-Host "shot: $shot"
}
finally {
  if ($ctx) {
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    if (-not $ctx.Host.HasExited) { $ctx.Host | Stop-Process -Force }
    Write-Host "host log: $(Join-Path $ctx.Data 'host.log')"
  }
  if ($explorer -ne [IntPtr]::Zero) { Close-TestExplorer $explorer }
  Remove-Item Env:EXPLORER_PANEL_DATA, Env:EXPLORER_PANEL_DEBUG, Env:EXPLORER_PANEL_UI, Env:EXPLORER_PANEL_ONLY_UNDER, Env:EXPLORER_PANEL_CLAUDE_DRY, Env:EP_E2E_INHERITED -ErrorAction SilentlyContinue
  if ($wasRunning) { Start-Process $installed }
}

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ("`n{0} passed, {1} failed" -f ($results.Count - $failed.Count), $failed.Count)
exit $(if ($failed.Count -gt 0) { 1 } else { 0 })
