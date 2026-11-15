# Общие функции сквозных проверок.
#
# Проверки идут на отдельном виртуальном рабочем столе "Claude tests" со своим окном Проводника:
#   * окна на рабочем столе пользователя не трогаются;
#   * окна не активируются (иначе Windows переключила бы рабочий стол пользователя);
#   * живой экран не снимается — снимаются только конкретные окна (tools/winshot);
#   * клавиши и мышь не имитируются; на странице панели действия выполняются через отладочный канал.
#
# Нужен модуль PowerShell VirtualDesktop:  Install-Module VirtualDesktop -Scope CurrentUser

$ErrorActionPreference = "Stop"
$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$PanelTitle = "ExplorerPanel"
$DesktopName = "Claude tests"

Add-Type -AssemblyName System.Drawing
if (-not ("E2E.Win" -as [type])) {
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
namespace E2E {
public static class Win {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h, IntPtr rgn);
  [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
  [DllImport("gdi32.dll")] public static extern int GetRegionData(IntPtr rgn, int size, byte[] data);
  [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
  public struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } }
    public override string ToString(){ return "[" + L + "," + T + " " + W + "x" + H + "]"; } }
  public static string Cls(IntPtr h){ var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
  public static List<IntPtr> Explorers() {
    var res = new List<IntPtr>();
    EnumWindows((h, l) => { if (Cls(h) == "CabinetWClass") res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
  // окно панели: окно верхнего уровня с нужным заголовком, владелец которого — данный Проводник
  public static IntPtr Panel(IntPtr explorer, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { if (Txt(h) == title && GetWindow(h, 4) == explorer) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static RECT Frame(IntPtr h){ RECT r; DwmGetWindowAttribute(h, 9, out r, 16); return r; }
  public static RECT Rect(IntPtr h){ RECT r; GetWindowRect(h, out r); return r; }
  public static bool OnOtherDesktop(IntPtr h){ int v; DwmGetWindowAttribute(h, 14, out v, 4); return v != 0; }
  // прямоугольники, из которых состоит область окна (в координатах окна)
  public static List<RECT> RegionRects(IntPtr h) {
    var res = new List<RECT>();
    var rgn = CreateRectRgn(0, 0, 0, 0);
    if (GetWindowRgn(h, rgn) > 1) {
      int size = GetRegionData(rgn, 0, null);
      var data = new byte[size];
      GetRegionData(rgn, size, data);
      int count = BitConverter.ToInt32(data, 8);
      for (int i = 0; i < count; i++) {
        int o = 32 + i * 16;
        res.Add(new RECT { L = BitConverter.ToInt32(data, o), T = BitConverter.ToInt32(data, o + 4), R = BitConverter.ToInt32(data, o + 8), B = BitConverter.ToInt32(data, o + 12) });
      }
    }
    DeleteObject(rgn);
    return res;
  }
}}
'@
}
$Win = [E2E.Win]

# ───────── тестовый рабочий стол и окно Проводника ─────────

function Get-TestDesktop {
  Import-Module VirtualDesktop -WarningAction SilentlyContinue
  $number = (Get-DesktopList | Where-Object Name -eq $DesktopName | Select-Object -First 1).Number
  if ($null -ne $number) { return Get-Desktop $number }
  $desktop = New-Desktop
  Set-DesktopName -Desktop $desktop -Name $DesktopName
  $desktop
}

# Открывает на тестовом рабочем столе новое окно Проводника с папкой $Path и возвращает его.
function New-TestExplorer([string]$Path) {
  $desktop = Get-TestDesktop
  $before = $Win::Explorers()
  # /n — обязательно новое окно: иначе Проводник может открыть вкладку в окне пользователя
  $new = $null
  foreach ($switch in "/n,", "/separate,") {
    Start-Process explorer.exe -ArgumentList ($switch + $Path)
    for ($i = 0; $i -lt 150 -and -not $new; $i++) {
      Start-Sleep -Milliseconds 20
      $new = $Win::Explorers() | Where-Object { $before -notcontains $_ } | Select-Object -First 1
    }
    if ($new) { break }
  }
  if (-not $new) { throw "a new Explorer window did not appear" }
  # окно только что создано — Windows может ещё не знать о нём, поэтому перенос повторяется
  $moved = $false
  for ($i = 0; $i -lt 60 -and -not $moved; $i++) {
    try { [void](Move-Window -Desktop $desktop -Hwnd $new -ErrorAction Stop); $moved = $true } catch { Start-Sleep -Milliseconds 50 }
  }
  if (-not $moved) { throw "could not move the Explorer window to the test desktop" }
  [void]$Win::ShowWindow($new, 4)   # показать без активации
  [void]$Win::SetWindowPos($new, [IntPtr]::Zero, 300, 120, 1300, 800, 0x0004 -bor 0x0010)
  Start-Sleep -Milliseconds 800
  $new
}

function Close-TestExplorer([IntPtr]$Explorer) {
  if ($Win::IsWindow($Explorer)) { [void]$Win::SendMessage($Explorer, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
}

function Remove-TestDesktop {
  Import-Module VirtualDesktop -WarningAction SilentlyContinue
  $number = (Get-DesktopList | Where-Object Name -eq $DesktopName | Select-Object -First 1).Number
  if ($null -ne $number) { Remove-Desktop (Get-Desktop $number) }
}

# ───────── действия над тестовым окном (без активации) ─────────

function Move-TestExplorer([IntPtr]$Explorer, [int]$Dx, [int]$Dy) {
  $r = $Win::Rect($Explorer)
  [void]$Win::SetWindowPos($Explorer, [IntPtr]::Zero, $r.L + $Dx, $r.T + $Dy, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
function Resize-TestExplorer([IntPtr]$Explorer, [int]$Dw, [int]$Dh) {
  $r = $Win::Rect($Explorer)
  [void]$Win::SetWindowPos($Explorer, [IntPtr]::Zero, 0, 0, $r.W + $Dw, $r.H + $Dh, 0x0002 -bor 0x0004 -bor 0x0010)
}
function Hide-TestExplorer([IntPtr]$Explorer) { [void]$Win::ShowWindow($Explorer, 7) }      # свернуть без активации
function Show-TestExplorer([IntPtr]$Explorer) { [void]$Win::ShowWindow($Explorer, 4) }      # развернуть без активации
function Set-TestExplorerFolder([IntPtr]$Explorer, [string]$Path) {
  $shell = New-Object -ComObject Shell.Application
  foreach ($window in $shell.Windows()) { if ([IntPtr]$window.HWND -eq $Explorer) { $window.Navigate2($Path); break } }
}

# ───────── панель ─────────

function Get-Panel([IntPtr]$Explorer) { $Win::Panel($Explorer, $PanelTitle) }

function Test-PanelVisible([IntPtr]$Explorer) {
  $panel = Get-Panel $Explorer
  ($panel -ne [IntPtr]::Zero) -and $Win::IsWindowVisible($panel) -and ($Win::RegionRects($panel).Count -gt 0)
}

# Ждёт, пока условие станет истинным; возвращает, дождались ли.
function Wait-Until([scriptblock]$Condition, [int]$TimeoutMs = 8000) {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
    if (& $Condition) { return $true }
    Start-Sleep -Milliseconds 100
  }
  [bool](& $Condition)
}

# Выполняет JavaScript на странице панели тестового окна (отладочный канал хозяина) и возвращает результат.
function Invoke-PanelJs([IntPtr]$Explorer, [string]$Script, [string]$DataDir) {
  $in = Join-Path $DataDir "debug-eval.js"; $out = Join-Path $DataDir "debug-eval.out"
  Remove-Item $out -ErrorAction SilentlyContinue
  Set-Content -Path $in -Value ("//target:" + [int64]$Explorer + "`n" + $Script) -Encoding UTF8
  for ($i = 0; $i -lt 80 -and -not (Test-Path $out); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $out)) { return "NO ANSWER" }
  $text = Get-Content $out -Raw
  try { $text | ConvertFrom-Json } catch { $text }
}

# Нажимает элемент панели по селектору CSS.
function Invoke-PanelClick([IntPtr]$Explorer, [string]$Selector, [string]$DataDir) {
  Invoke-PanelJs $Explorer "(() => { const e = document.querySelector('$Selector'); if (!e) return 'not found'; e.click(); return 'clicked' })()" $DataDir
}

# Снимок тестового окна вместе с панелью — так, как их рисует Windows (с настоящей прозрачностью).
function Save-Shot([IntPtr]$Explorer, [string]$Out) {
  $exe = Join-Path $Repo "tools\winshot\bin\Release\net10.0-windows10.0.22621.0\WinShot.exe"
  if (-not (Test-Path $exe)) { dotnet build (Join-Path $Repo "tools\winshot") -c Release --nologo -v q | Out-Null }
  $panel = Get-Panel $Explorer
  if ($panel -ne [IntPtr]::Zero -and $Win::IsWindowVisible($panel)) { & $exe $Out ([int64]$Explorer) ([int64]$panel) | Out-Null }
  else { & $exe $Out ([int64]$Explorer) | Out-Null }
  $Out
}
