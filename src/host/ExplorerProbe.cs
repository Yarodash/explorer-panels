using System.Runtime.InteropServices;

namespace ExplorerPanel.Host;

/// <summary>Что показывает одно окно Проводника.</summary>
/// <param name="Window">Окно верхнего уровня (общее для всех его вкладок).</param>
/// <param name="Folder">Папка активной вкладки; null или не-путь для особых страниц («Этот компьютер», «Главная»).</param>
sealed record ExplorerView(IntPtr Window, string? Folder, bool Minimized);

/// <summary>Спрашивает у самого Проводника, какие окна открыты и что в их активных вкладках.</summary>
static class ExplorerProbe
{
    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IComServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleWindow
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    }

    static Guid _topLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static Guid _shellBrowser = new("000214E2-0000-0000-C000-000000000046");
    static dynamic? _shell;

    /// <summary>
    /// Окна Проводника с папкой активной вкладки. Бросает исключение, если Проводник недоступен
    /// (перезапускается или ещё не запущен) — вызывающий повторит позже.
    /// </summary>
    public static List<ExplorerView> Views()
    {
        var folders = new Dictionary<IntPtr, string?>();
        try
        {
            _shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")
                                                ?? throw new InvalidOperationException("Shell.Application is not available"));
            dynamic windows = _shell!.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic? window = windows.Item(i);
                    if (window == null) continue;
                    var top = new IntPtr(Convert.ToInt64(window.HWND));
                    if (Native.ClassName(top) != "CabinetWClass") continue;

                    // У всех вкладок окна один и тот же HWND. Активная вкладка — та, чьё окно вкладки сейчас верхнее.
                    IntPtr activeTab = Native.FindWindowEx(top, IntPtr.Zero, "ShellTabWindowClass", null);
                    IntPtr thisTab = TabWindow((object)window);
                    if (activeTab != IntPtr.Zero && thisTab != IntPtr.Zero && thisTab != activeTab) continue;

                    string? folder = null;
                    try { folder = (string)window.Document.Folder.Self.Path; } catch { }
                    folders[top] = folder;
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // окно закрылось, пока мы его опрашивали
                }
            }
        }
        catch
        {
            _shell = null; // Проводник мог перезапуститься — в следующий раз подключимся заново
            throw;
        }
        return folders.Select(pair => new ExplorerView(pair.Key, pair.Value, Native.IsIconic(pair.Key))).ToList();
    }

    static IntPtr TabWindow(object shellWindow)
    {
        try
        {
            var provider = (IComServiceProvider)shellWindow;
            if (provider.QueryService(ref _topLevelBrowser, ref _shellBrowser, out IntPtr browser) != 0 || browser == IntPtr.Zero)
                return IntPtr.Zero;
            try
            {
                var oleWindow = (IOleWindow)Marshal.GetObjectForIUnknown(browser);
                return oleWindow.GetWindow(out IntPtr hwnd) == 0 ? hwnd : IntPtr.Zero;
            }
            finally { Marshal.Release(browser); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Область со списком файлов активной вкладки (в координатах экрана). Окна неактивных вкладок скрыты,
    /// поэтому берётся первое видимое.
    /// </summary>
    public static bool TryGetFileArea(IntPtr explorer, out Native.RECT area)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumChildWindows(explorer, (hwnd, _) =>
        {
            if (Native.ClassName(hwnd) != "SHELLDLL_DefView" || !Native.IsWindowVisible(hwnd)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        area = default;
        return found != IntPtr.Zero && Native.GetWindowRect(found, out area) && area.Width > 0 && area.Height > 0;
    }

    /// <summary>Рабочая область монитора, на котором находится окно (экран без панели задач).</summary>
    public static bool TryGetWorkArea(IntPtr hwnd, out Native.RECT work)
    {
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        bool ok = Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref info);
        work = info.rcWork;
        return ok;
    }
}
