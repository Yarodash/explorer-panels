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
    readonly Dictionary<IntPtr, Shown> _shown = []; // окно Проводника -> что в нём сейчас показано

    /// <summary>Что показано в окне Проводника.</summary>
    /// <param name="Project">Проект, чья панель показана; null — у папки нет панели, показаны только кнопки Claude.</param>
    /// <param name="Folder">Папка, открытая в окне (в ней кнопки Claude открывают терминал).</param>
    /// <param name="Claude">Показаны ли кнопки Claude.</param>
    sealed record Shown(Project? Project, string Folder, bool Claude);
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
    readonly HostSettings _settings = new(AppPaths.SettingsFile);
    Tray? _tray;
    ApiServer? _api;

    public TrustStore Trust { get; } = new(AppPaths.TrustFile);
    public ProjectRegistry Projects { get; }

    /// <summary>
    /// Включены ли панели (переключается из трея, сохраняется между запусками).
    /// Когда выключены, все панели скрыты; серверы при этом продолжают работать.
    /// </summary>
    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (value == _settings.Enabled) return;
            _settings.Enabled = value;
            Log.Write(value ? "panels turned on" : "panels turned off");
            Rescan();
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Показывать ли кнопки Claude во всех папках (переключается из трея, сохраняется между запусками).
    /// </summary>
    public bool ClaudeButtons
    {
        get => _settings.ClaudeButtons;
        set
        {
            if (value == _settings.ClaudeButtons) return;
            _settings.ClaudeButtons = value;
            Log.Write(value ? "claude buttons turned on" : "claude buttons turned off");
            Rescan();
            StateChanged?.Invoke();
        }
    }

    /// <summary>Сколько панелей проектов сейчас на экране (папки, где показаны только кнопки Claude, не в счёт).</summary>
    public int ShownPanels => _shown.Values.Count(shown => shown.Project != null);

    /// <summary>Что-то из показываемого в трее могло измениться (приходит в потоке окон).</summary>
    public event Action? StateChanged;

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
            bool usable = Enabled && !view.Minimized;
            // Панель проекта показывается, если у папки есть рабочий panel.json и папка не запрещена.
            // Для папки, о которой владелец ещё не решал, страница показывает вопрос «разрешить?».
            bool panel = usable && project is { Config: not null } && project.Trusted != false
                         && ProjectLocator.Applies(project.Root, view.Folder!, project.Config.Scope);
            // Кнопки Claude — в любой настоящей папке, есть у неё панель или нет.
            bool claude = usable && ClaudeButtons && ClaudeLauncher.CanOpenIn(view.Folder);
            bool show = panel || claude;

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
                var shown = new Shown(panel ? project : null, view.Folder!, claude);
                _shown[view.Window] = shown;
                if (panel) visible.Add(project!);
                window.Post(StateMessage(shown));
            }
            else _shown.Remove(view.Window);
            window.SetVisible(show);
            if (show) window.Reposition();
        }

        foreach (var project in Projects.All) project.SetActive(visible.Contains(project));

        // Экономия в простое: если давно ничего не показано, окна с браузерным движком закрываются.
        // Понадобятся снова — создадутся заново.
        if (_shown.Count > 0) _lastVisible = DateTime.UtcNow;
        else if (_windows.Count > 0 && DateTime.UtcNow - _lastVisible > ReleaseEngineAfter)
        {
            foreach (var window in _windows.Values) window.Dispose();
            _windows.Clear();
            _shown.Clear();
            Log.Write("no panels visible for a while: browser engine released");
        }

        StateChanged?.Invoke();
    }

    string StateMessage(Shown shown)
    {
        var state = shown.Project?.BuildState(_locale) ?? PanelStates.Bare(shown.Folder, _locale);
        state["claude"] = shown.Claude;
        return new JsonObject { ["type"] = "state", ["state"] = state }.ToJsonString();
    }

    // Проект изменился: обновляем все окна, где показана его панель.
    void PushState(Project project)
    {
        foreach (var (explorer, shown) in _shown)
        {
            if (shown.Project == project && _windows.TryGetValue(explorer, out var window)) window.Post(StateMessage(shown));
        }
    }

    // Сообщение от страницы панели: нажали кнопку, переключили тумблер, ответили на вопрос о разрешении.
    void OnPageMessage(PanelWindow window, JsonElement message)
    {
        if (!_shown.TryGetValue(window.Explorer, out var shown)) return;
        var type = message.GetProperty("type").GetString();
        if (type == "claude")
        {
            if (shown.Claude) OpenClaude(shown.Folder, message.GetProperty("resume").GetBoolean());
            return;
        }
        if (shown.Project is not { } project) return; // остальные сообщения относятся к панели проекта
        switch (type)
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

    // Папку страница не присылает: Claude открывается в той, что программа сама видит в окне Проводника.
    static void OpenClaude(string folder, bool resume)
    {
        Log.Write($"claude{(resume ? " --continue" : "")}: opening in {folder}");
        // сквозные проверки не открывают окон на рабочем столе пользователя — им достаточно записи в журнале
        if (Environment.GetEnvironmentVariable("EXPLORER_PANEL_CLAUDE_DRY") == "1") return;
        try { ClaudeLauncher.Open(folder, resume); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write("claude did not start: " + ex.Message);
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
