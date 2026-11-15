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

    // Для сквозных проверок: EXPLORER_PANEL_ONLY=<номер окна> ограничивает программу одним окном Проводника,
    // чтобы проверочный экземпляр не трогал окна пользователя.
    readonly long _onlyWindow = long.TryParse(Environment.GetEnvironmentVariable("EXPLORER_PANEL_ONLY"), out long only) ? only : 0;

    public TrustStore Trust { get; } = new(AppPaths.TrustFile);
    public ProjectRegistry Projects { get; }

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
        if (_onlyWindow != 0) views = views.Where(v => v.Window.ToInt64() == _onlyWindow).ToList();

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
            bool show = project is { Config: not null } && project.Trusted != false && !view.Minimized
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
