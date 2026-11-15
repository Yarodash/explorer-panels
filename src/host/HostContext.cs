using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExplorerPanel.Core;

namespace ExplorerPanel.Host;

/// <summary>
/// Главный объект фоновой программы: связывает слежение за Проводником, проекты и окна панелей.
/// Всё здесь выполняется в одном потоке (потоке окон); события проектов переносятся в него.
/// </summary>
sealed class HostContext : ApplicationContext
{
    readonly Control _ui = new(); // через него события из других потоков попадают в поток окон
    readonly ExplorerTracker _tracker = new();
    readonly Dictionary<IntPtr, PanelWindow> _windows = [];
    readonly Dictionary<IntPtr, Project> _shown = []; // окно Проводника -> проект, чья панель в нём сейчас показана
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 700 };
    readonly string _uiFolder;
    readonly string _locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    int _tick;

    // Для сквозных проверок: EXPLORER_PANEL_ONLY_UNDER=<папка> ограничивает программу окнами Проводника,
    // открытыми в этой папке и её подпапках, — проверочный экземпляр не трогает окна пользователя.
    readonly string? _onlyUnder = Environment.GetEnvironmentVariable("EXPLORER_PANEL_ONLY_UNDER") is { Length: > 0 } under
        ? ProjectLocator.Normalize(under)
        : null;

    static readonly TimeSpan ReleaseEngineAfter = TimeSpan.FromMinutes(5);
    DateTime _lastVisible = DateTime.UtcNow; // когда в последний раз была видна хоть одна панель
    bool _paused;
    Tray? _tray;
    ApiServer? _api;

    public TrustStore Trust { get; } = new(AppPaths.TrustFile);
    public ProjectRegistry Projects { get; }

    /// <summary>Пауза из меню в трее: все панели скрыты, серверы продолжают работать.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            Rescan();
        }
    }

    public HostContext()
    {
        _ = _ui.Handle; // создаём окно-посредник в этом потоке
        _uiFolder = Environment.GetEnvironmentVariable("EXPLORER_PANEL_UI") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(AppContext.BaseDirectory, "ui");
        Log.Write($"started {AppInfo.Version}, ui={_uiFolder}");

        Projects = new ProjectRegistry(Trust, new WindowsShell());
        Projects.Changed += project => RunOnUi(() => PushState(project));

        _tracker.Changed += OnExplorerChanged;
        _tracker.Moved += explorer => { if (_windows.TryGetValue(explorer, out var window)) window.Reposition(); };
        _tracker.Start();

        _refresh.Tick += (_, _) => RefreshProjects();
        _refresh.Start();

        _tray = new Tray(this);

        // местный интерфейс для команды explorer-panel и внешних программ
        _api = new ApiServer(new HostApi(this), AppPaths.HostInfoFile);
        try { _api.Start(); }
        catch (Exception ex) { Log.Write("local interface did not start: " + ex.Message); }
    }

    /// <summary>Выполняет действие в потоке окон (можно вызывать из любого потока).</summary>
    public void RunOnUi(Action action)
    {
        try
        {
            if (_ui.IsHandleCreated && !_ui.IsDisposed) _ui.BeginInvoke(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // программа завершается
        }
    }

    /// <summary>Заново сверяет окна Проводника с панелями (после изменения разрешений и подобного).</summary>
    public void Rescan() => _tracker.Rescan();

    void OnExplorerChanged(IReadOnlyList<ExplorerView> views)
    {
        if (_onlyUnder != null)
            views = views.Where(v => v.Folder != null && Path.IsPathFullyQualified(v.Folder)
                                     && ProjectLocator.Applies(_onlyUnder, v.Folder, "tree")).ToList();

        // окна панелей, чей Проводник закрылся
        foreach (var explorer in _windows.Keys.Except(views.Select(v => v.Window)).ToList())
        {
            _windows[explorer].Dispose();
            _windows.Remove(explorer);
            _shown.Remove(explorer);
        }

        var visible = new HashSet<Project>();
        foreach (var view in views)
        {
            var project = Projects.ForFolder(view.Folder);
            // Панель показывается, если у папки есть рабочий panel.json, папка не запрещена и окно не свёрнуто.
            // Для папки, о которой владелец ещё не решал, страница показывает вопрос «разрешить?».
            bool show = !_paused && project is { Config: not null } && project.Trusted != false && !view.Minimized
                        && ProjectLocator.Applies(project.Root, view.Folder!, project.Config.Scope);

            if (!_windows.TryGetValue(view.Window, out var window))
            {
                if (!show) continue;
                window = new PanelWindow(view.Window, _uiFolder);
                var created = window;
                window.Message += message => OnPageMessage(created, message);
                _windows[view.Window] = window;
            }

            if (show)
            {
                _shown[view.Window] = project!;
                visible.Add(project!);
                window.Post(StateMessage(project!));
            }
            else _shown.Remove(view.Window);
            window.SetVisible(show);
            if (show) window.Reposition();
        }

        foreach (var project in Projects.All) project.SetActive(visible.Contains(project));

        // Экономия в простое: если панелей давно не видно, окна с браузерным движком закрываются.
        // Понадобятся снова — создадутся заново.
        if (visible.Count > 0) _lastVisible = DateTime.UtcNow;
        else if (_windows.Count > 0 && DateTime.UtcNow - _lastVisible > ReleaseEngineAfter)
        {
            foreach (var window in _windows.Values) window.Dispose();
            _windows.Clear();
            _shown.Clear();
            Log.Write("no panels visible for a while: browser engine released");
        }
    }

    string StateMessage(Project project) =>
        new JsonObject { ["type"] = "state", ["state"] = project.BuildState(_locale) }.ToJsonString();

    // Проект изменился: обновляем все окна, где показана его панель.
    void PushState(Project project)
    {
        string? message = null;
        foreach (var (explorer, shown) in _shown)
        {
            if (shown != project || !_windows.TryGetValue(explorer, out var window)) continue;
            window.Post(message ??= StateMessage(project));
        }
    }

    // Сообщение от страницы панели: нажали кнопку, переключили тумблер, ответили на вопрос о разрешении.
    void OnPageMessage(PanelWindow window, JsonElement message)
    {
        if (!_shown.TryGetValue(window.Explorer, out var project)) return;
        switch (message.GetProperty("type").GetString())
        {
            case "do":
                var action = message.GetProperty("action").GetString()!;
                Dictionary<string, string>? parameters = null;
                if (message.TryGetProperty("params", out var given) && given.ValueKind == JsonValueKind.Object)
                    parameters = given.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
                _ = Task.Run(async () =>
                {
                    var result = await project.DoAsync(action, parameters);
                    if (!result.Ok) Log.Write($"action {action} in {project.Root}: {result.Error}");
                });
                break;
            case "set":
                project.SetValue(message.GetProperty("name").GetString()!, JsonNode.Parse(message.GetProperty("value").GetRawText()));
                break;
            case "trust":
                project.SetTrusted(message.GetProperty("allow").GetBoolean());
                Rescan(); // запрещённая панель должна исчезнуть сразу
                break;
            case "collapse":
                project.SetCollapsed(message.GetProperty("collapsed").GetBoolean());
                break;
            case "error":
                project.LastError = message.GetProperty("message").GetString();
                break;
        }
    }

    // Раз в 0,7 с: проекты с видимой панелью проверяются каждый раз; невидимые, но с запущенными панелью
    // серверами — примерно раз в 10 с; остальные не трогаются вовсе.
    void RefreshProjects()
    {
        _tick++;
        foreach (var project in Projects.All)
        {
            if (project.Active || (_tick % 14 == 0 && project.HasOwnProcesses))
                _ = Task.Run(project.RefreshAsync);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _api?.Dispose();
            _tray?.Dispose();
            _refresh.Dispose();
            _tracker.Dispose();
            foreach (var window in _windows.Values) window.Dispose();
            _windows.Clear();
            Projects.Dispose(); // серверы при этом продолжают работать
            _ui.Dispose();
        }
        base.Dispose(disposing);
    }
}
