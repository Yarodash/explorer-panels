using System.Text.Json;
using ExplorerPanel.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ExplorerPanel.Host;

/// <summary>
/// Окно панели, приклеенное к одному окну Проводника. Внутри — браузерный движок (WebView2) со страницей панели.
///
/// Как оно становится «частью Проводника»:
///   * владелец окна — окно Проводника: Windows сама сворачивает панель вместе с ним и переносит на другой рабочий стол;
///   * в стопке окон панель стоит сразу над владельцем, а не поверх всех;
///   * окно — прозрачный «холст» в правом нижнем углу области файлов, больше самой панели: в нём есть место
///     подсказкам, календарю и меню;
///   * у окна нет собственной поверхности, на экране только картинка страницы с настоящей прозрачностью;
///   * страница каждый кадр сообщает прямоугольники своих видимых частей, и окно обрезается по ним —
///     клики мимо панели проходят в Проводник.
/// </summary>
sealed class PanelWindow : IDisposable
{
    // размеры при масштабе 100%
    const double CanvasMaxWidth = 1400, CanvasMaxHeight = 520;
    const double MarginRight = 24;    // чтобы не залезать на полосу прокрутки
    const double MarginBottom = 8;
    const double PagePadding = 8;     // панель на странице отступает от края холста на 8 (под тень)
    const double StatusBarHeight = 24;
    const int RegionPadding = 14;     // запас вокруг видимых частей под тени

    static Task<CoreWebView2Environment>? _environment;
    static readonly List<PanelWindow> Live = [];
    static System.Windows.Forms.Timer? _debugTimer;

    readonly CanvasForm _form;
    readonly WebView2 _web;
    string? _state;      // последнее состояние; отправляется странице, как только она готова
    bool _pageReady, _disposed;

    public IntPtr Explorer { get; }

    /// <summary>Сообщения страницы (do, set, trust, collapse…); область и расширение окно обрабатывает само.</summary>
    public event Action<JsonElement>? Message;

    // Окно-холст: без рамки, не забирает фокус, не видно в панели задач и Alt+Tab.
    sealed class CanvasForm : Form
    {
        readonly IntPtr _owner;

        public CanvasForm(IntPtr owner)
        {
            _owner = owner;
            Text = "ExplorerPanel";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            // ShowInTaskbar = false здесь нельзя: тогда WinForms подставляет окну собственного скрытого владельца.
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Parent = _owner; // у окна верхнего уровня это «владелец»
                cp.Style |= Native.WS_POPUP;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOREDIRECTIONBITMAP;
                cp.ExStyle &= ~Native.WS_EX_APPWINDOW; // WinForms добавляет его сам — из-за него панель была бы отдельным окном
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // пока страница не сообщила, где её видимые части, окно целиком «не существует»
            Native.SetWindowRgn(Handle, Native.CreateRectRgn(0, 0, 0, 0), false);
        }
    }

    public PanelWindow(IntPtr explorer, string uiFolder)
    {
        Explorer = explorer;
        _form = new CanvasForm(explorer);
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
        _form.Controls.Add(_web);
        _form.FormClosed += (_, _) => { _disposed = true; Live.Remove(this); };

        _ = _form.Handle; // создаёт окно
        Reposition();
        Live.Add(this);
        StartDebugChannel();
        _ = InitAsync(uiFolder);
    }

    IntPtr Hwnd => _form.Handle;

    public bool Alive => !_disposed && !_form.IsDisposed && Native.IsWindow(_form.Handle);

    double Scale => Native.GetDpiForWindow(Explorer) / 96.0;

    async Task InitAsync(string uiFolder)
    {
        try
        {
            _environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, "WebView2"));
            await _web.EnsureCoreWebView2Async(await _environment);
            if (_disposed) return;

            // собранная страница отдаётся как https://panel.local/
            var core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("panel.local", uiFolder, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.WebMessageReceived += OnMessage;
            core.Navigate("https://panel.local/index.html");
        }
        catch (Exception ex)
        {
            Log.Write($"panel window for 0x{Explorer:X}: browser engine failed to start: {ex}");
        }
    }

    // ───────── обмен со страницей ─────────

    /// <summary>Отправляет странице состояние панели (JSON сообщения "state"). До готовности страницы — запоминает.</summary>
    public void Post(string stateMessageJson)
    {
        if (stateMessageJson == _state) return;
        _state = stateMessageJson;
        if (Alive && _pageReady && _web.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(stateMessageJson);
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "region":
                    ApplyRegion(root);
                    break;
                case "fit":
                    FitExplorer(root.GetProperty("width").GetDouble(), root.GetProperty("dpr").GetDouble());
                    break;
                case "ready":
                    _pageReady = true;
                    if (_state != null) _web.CoreWebView2.PostWebMessageAsJson(_state);
                    break;
                case "release":
                    // Нажатие на панели закончилось, и всплывающих частей на ней нет. Если фокус при этом остался
                    // у панели (его забрал браузерный движок) — возвращаем Проводнику. Активное окно другой
                    // программы не трогаем: отбирать фокус у того, с чем человек работает, нельзя.
                    if (Native.GetForegroundWindow() == Hwnd) Native.SetForegroundWindow(Explorer);
                    break;
                default:
                    Message?.Invoke(root.Clone());
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write("page message failed: " + ex.Message);
        }
    }

    // ───────── положение, видимость, порядок окон ─────────

    public void SetVisible(bool visible)
    {
        if (!Alive || Native.IsWindowVisible(Hwnd) == visible) return;
        if (visible)
        {
            Reposition();
            Native.ShowWindow(Hwnd, Native.SW_SHOWNOACTIVATE);
            EnsureOwner();
            PlaceAboveOwner();
        }
        else Native.ShowWindow(Hwnd, Native.SW_HIDE);
    }

    // WinForms при создании и показе формы сбрасывает ей владельца — возвращаем окно Проводника.
    void EnsureOwner()
    {
        if (Native.GetWindowLongPtr(Hwnd, Native.GWLP_HWNDPARENT) != Explorer)
            Native.SetWindowLongPtr(Hwnd, Native.GWLP_HWNDPARENT, Explorer);
    }

    // Только что показанное окно Windows кладёт на самый верх стопки — панель всплыла бы поверх браузера,
    // даже если её Проводник лежит под ним. Ставим панель сразу над своим Проводником.
    void PlaceAboveOwner()
    {
        IntPtr above = Native.GetWindow(Explorer, Native.GW_HWNDPREV); // окно прямо над Проводником
        if (above == Hwnd) return;
        // above == 0 значит, что Проводник сам верхний: тогда панель — на самый верх
        Native.SetWindowPos(Hwnd, above, 0, 0, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Ставит холст в правый нижний угол области файлов. Холст не вылезает за её левый и верхний края:
    /// панель не залезает на дерево папок и не обрезается краем окна.
    /// </summary>
    public void Reposition()
    {
        if (!Alive) return;
        EnsureOwner();
        double s = Scale;
        if (!ExplorerProbe.TryGetFileArea(Explorer, out var area))
        {
            // область файлов не нашлась — берём всё окно без строки состояния
            if (Native.DwmGetWindowAttribute(Explorer, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out area, 16) != 0) return;
            area.R -= 1;
            area.B -= 1 + (int)Math.Round(StatusBarHeight * s);
        }
        int right = area.R - (int)Math.Round((MarginRight - PagePadding) * s);
        int bottom = area.B - (int)Math.Round((MarginBottom - PagePadding) * s);
        int width = Math.Clamp(right - area.L, (int)(120 * s), (int)Math.Round(CanvasMaxWidth * s));
        int height = Math.Clamp(bottom - area.T, (int)(60 * s), (int)Math.Round(CanvasMaxHeight * s));
        int x = right - width, y = bottom - height;

        Native.GetWindowRect(Hwnd, out var current);
        if (x == current.L && y == current.T && width == current.Width && height == current.Height) return;
        Native.SetWindowPos(Hwnd, IntPtr.Zero, x, y, width, height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    // Страница сообщила, где сейчас её видимые части (в пикселях страницы): обрезаем окно по ним.
    void ApplyRegion(JsonElement message)
    {
        if (!Alive) return;
        double dpr = message.GetProperty("dpr").GetDouble();
        var region = Native.CreateRectRgn(0, 0, 0, 0);
        foreach (var rect in message.GetProperty("rects").EnumerateArray())
        {
            double x = rect[0].GetDouble(), y = rect[1].GetDouble(), width = rect[2].GetDouble(), height = rect[3].GetDouble();
            var part = Native.CreateRectRgn(
                (int)Math.Floor(x * dpr) - RegionPadding, (int)Math.Floor(y * dpr) - RegionPadding,
                (int)Math.Ceiling((x + width) * dpr) + RegionPadding, (int)Math.Ceiling((y + height) * dpr) + RegionPadding);
            Native.CombineRgn(region, region, part, Native.RGN_OR);
            Native.DeleteObject(part);
        }
        Native.SetWindowRgn(Hwnd, region, false); // после вызова областью владеет Windows; перерисовывать нечего
    }

    // Кнопка «расширить»: панели нужно neededCssWidth пикселей страницы, а холст уже.
    // Увеличиваем окно Проводника ровно на недостающую ширину (вправо; если там край экрана — сдвигаем влево).
    void FitExplorer(double neededCssWidth, double dpr)
    {
        if (!Alive || Native.IsZoomed(Explorer)) return; // развёрнутое на весь экран окно шире не сделать
        Native.GetWindowRect(Hwnd, out var canvas);
        int missing = (int)Math.Ceiling(neededCssWidth * dpr) - canvas.Width;
        if (missing <= 0) return;

        Native.GetWindowRect(Explorer, out var window);
        int width = window.Width + missing, left = window.L;
        if (ExplorerProbe.TryGetWorkArea(Explorer, out var work))
        {
            width = Math.Min(width, work.Width);
            if (left + width > work.R) left = Math.Max(work.L, work.R - width);
        }
        Native.SetWindowPos(Explorer, IntPtr.Zero, left, window.T, width, window.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    // ───────── отладка ─────────

    // Канал для автоматических проверок (только при EXPLORER_PANEL_DEBUG=1): JavaScript из файла
    // <папка данных>\debug-eval.js выполняется на странице, результат пишется в debug-eval.out.
    // Первая строка "//target:<число>" — выполнить только на панели этого окна Проводника.
    static void StartDebugChannel()
    {
        if (_debugTimer != null || Environment.GetEnvironmentVariable("EXPLORER_PANEL_DEBUG") != "1") return;
        string input = Path.Combine(AppPaths.DataDir, "debug-eval.js"), output = Path.Combine(AppPaths.DataDir, "debug-eval.out");
        _debugTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _debugTimer.Tick += async (_, _) =>
        {
            if (!File.Exists(input)) return;
            string script;
            try
            {
                script = File.ReadAllText(input);
                File.Delete(input);
            }
            catch (IOException) { return; }

            long only = 0;
            if (script.StartsWith("//target:")) long.TryParse(script[9..script.IndexOf('\n')].Trim(), out only);
            // Строка "//mouse:x,y" после цели — не JavaScript, а настоящее нажатие мыши в точке страницы
            // (через протокол отладки браузера): так проверяется то, что программный click() не воспроизводит.
            var body = script.StartsWith("//target:") ? script[(script.IndexOf('\n') + 1)..] : script;
            var result = "NO PANEL";
            foreach (var window in Live.ToList())
            {
                if (only != 0 && window.Explorer.ToInt64() != only) continue;
                try
                {
                    var core = window._web.CoreWebView2;
                    if (core == null) result = "PAGE NOT READY";
                    else if (body.StartsWith("//mouse:"))
                    {
                        var xy = body[8..].Trim().Split(',');
                        string Event(string type) =>
                            $"{{\"type\":\"{type}\",\"x\":{xy[0]},\"y\":{xy[1]},\"button\":\"left\",\"buttons\":{(type == "mouseReleased" ? 0 : 1)},\"clickCount\":1,\"pointerType\":\"mouse\"}}";
                        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", $"{{\"type\":\"mouseMoved\",\"x\":{xy[0]},\"y\":{xy[1]}}}");
                        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Event("mousePressed"));
                        await Task.Delay(90);
                        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Event("mouseReleased"));
                        result = "\"pressed\"";
                    }
                    else result = await core.ExecuteScriptAsync(script);
                }
                catch (Exception ex) { result = "ERROR " + ex.Message; }
            }
            try { File.WriteAllText(output, result); } catch (IOException) { }
        };
        _debugTimer.Start();
    }

    public void Dispose()
    {
        Live.Remove(this);
        if (_disposed) return;
        _disposed = true;
        try
        {
            _web.Dispose();
            _form.Close();
            _form.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
