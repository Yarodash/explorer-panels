using System.Text.Json;
using System.Text.Json.Nodes;
using ExplorerPanel.Core;
using ExplorerPanel.Core.Api;

namespace ExplorerPanel.Host;

/// <summary>
/// Один проект: папка с <c>.explorer_components/panel.json</c>, её серверы, действия и значения.
/// Методы можно вызывать из любого потока; событие <see cref="Changed"/> тоже приходит из любого потока.
/// </summary>
sealed class Project : IDisposable
{
    static readonly TimeSpan NeedsTimeout = TimeSpan.FromSeconds(90);
    static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(15);
    static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan ProgressShownFor = TimeSpan.FromSeconds(2.5);

    readonly TrustStore _trust;
    readonly IShell _shell;
    readonly StateFile _state;
    readonly ValueStore _values;
    readonly RotatingLog _log;
    readonly object _gate = new();
    readonly Dictionary<string, ServiceRunner> _services = [];
    readonly Dictionary<string, string> _progress = [];     // действие -> "running" | "ok" | "failed"
    readonly Dictionary<string, JsonArray> _slots = [];     // значение -> виджеты, которые вернул его обработчик
    readonly Dictionary<string, DateTime> _handlerDue = []; // значение -> когда запускать обработчик снова
    DateTime _fileTime;
    bool _active, _prepared;
    int _refreshing;

    public string Root { get; }
    public string LogFile { get; }

    /// <summary>Итог последнего чтения panel.json (может быть с ошибками).</summary>
    public LoadResult Loaded { get; private set; } = new(null, []);

    /// <summary>Последняя рабочая версия panel.json; null, если файла нет или он ни разу не прочитался без ошибок.</summary>
    public PanelConfig? Config { get; private set; }

    /// <summary>Последняя ошибка, возникшая при показе панели (сообщает страница).</summary>
    public string? LastError { get; set; }

    public bool? Trusted => _trust.Get(Root);

    /// <summary>Что-то изменилось: статусы, значения, действия, сам файл.</summary>
    public event Action? Changed;

    public Project(string root, TrustStore trust, string stateFile, IShell shell)
    {
        Root = ProjectLocator.Normalize(root);
        _trust = trust;
        _shell = shell;
        _state = new StateFile(stateFile);
        _values = new ValueStore(_state);
        _values.Changed += RaiseChanged;
        LogFile = Path.Combine(Root, PanelLoader.DirName, "logs", "services.log");
        _log = new RotatingLog(LogFile);
        Reload();
    }

    void RaiseChanged() => Changed?.Invoke();

    // ───────── файл панели ─────────

    /// <summary>
    /// Перечитывает panel.json. Если в новой версии ошибки, панель продолжает работать на прежней:
    /// редактор или агент мог сохранить половину файла.
    /// </summary>
    public void Reload()
    {
        var file = PanelLoader.PanelFile(Root);
        var loaded = PanelLoader.LoadFile(Root);
        lock (_gate)
        {
            _fileTime = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : default;
            Loaded = loaded;
            if (_fileTime == default) Config = null;      // файла нет — панели нет
            else if (loaded.Ok) Apply(loaded.Config!);
        }
        RaiseChanged();
    }

    /// <summary>
    /// Перечитывает файл, если он изменился с прошлого раза. Вызывается при каждом обходе окон Проводника
    /// (раз в секунду для видимых папок). Отдельного слежения за папкой нет намеренно: оно держало бы папку
    /// проекта открытой, и её нельзя было бы переименовать, пока программа работает.
    /// </summary>
    public void ReloadIfChanged()
    {
        var file = PanelLoader.PanelFile(Root);
        var time = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : default;
        bool changed;
        lock (_gate) changed = time != _fileTime;
        if (changed) Reload();
    }

    void Apply(PanelConfig config)
    {
        Config = config;

        foreach (var def in config.Services)
        {
            if (_services.TryGetValue(def.Name, out var existing)) existing.UpdateDef(def);
            else
            {
                var runner = new ServiceRunner(Root, def, _log, _state);
                runner.Changed += RaiseChanged;
                _services[def.Name] = runner;
            }
        }
        foreach (var removed in _services.Keys.Except(config.Services.Select(s => s.Name)).ToList()) _services.Remove(removed);

        foreach (var value in config.Values)
        {
            // у хранимого значения есть начальное; результат обработчика появится после первого запуска
            if (value.Run == null && value.Default != null && _values.Get(value.Name) == null)
                _values.Set(value.Name, value.Default, persist: false);
        }
        foreach (var gone in _slots.Keys.Except(config.Values.Select(v => v.Name)).ToList()) _slots.Remove(gone);
        _handlerDue.Clear(); // после изменения файла обработчики запускаются заново
    }

    // ───────── состояние ─────────

    public (ServiceStatus Status, string? Detail) ServiceStatus(string name)
    {
        lock (_gate) return _services.TryGetValue(name, out var runner) ? runner.Current : (Core.ServiceStatus.Stopped, null);
    }

    /// <summary>Есть ли серверы, запущенные панелью: за ними нужно следить, даже когда панель не видна.</summary>
    public bool HasOwnProcesses
    {
        get
        {
            List<ServiceRunner> runners;
            lock (_gate) runners = _services.Values.ToList();
            return runners.Any(r => r.OwnAlive());
        }
    }

    /// <summary>Сколько серверов проекта сейчас работает из запущенных панелью.</summary>
    public int OwnProcessCount
    {
        get
        {
            List<ServiceRunner> runners;
            lock (_gate) runners = _services.Values.ToList();
            return runners.Count(r => r.OwnAlive());
        }
    }

    public bool Active => _active;

    /// <summary>Видна ли сейчас панель проекта. Обработчики значений запускаются, только пока она видна.</summary>
    public void SetActive(bool active) => _active = active;

    string? ValueText(string name) => _values.GetText(name);

    /// <summary>Состояние для страницы панели (тип PanelState в src/ui/src/bridge.ts).</summary>
    public JsonObject BuildState(string locale)
    {
        lock (_gate)
        {
            var services = new JsonObject();
            foreach (var (name, runner) in _services)
            {
                var (status, detail) = runner.Current;
                var info = new JsonObject { ["status"] = status.ToString().ToLowerInvariant() };
                if (detail != null) info["detail"] = detail;
                services[name] = info;
            }
            var actions = new JsonObject();
            foreach (var (name, progress) in _progress) actions[name] = progress;
            var slots = new JsonObject();
            foreach (var (name, widgets) in _slots) slots[name] = widgets.DeepClone();
            var problems = new JsonArray();
            foreach (var problem in Loaded.Problems.Where(p => p.IsError)) problems.Add(problem.ToString());

            return new JsonObject
            {
                ["root"] = Root,
                ["locale"] = locale,
                ["trusted"] = Trusted is { } trusted ? JsonValue.Create(trusted) : null,
                ["collapsed"] = _state.Root["collapsed"]?.GetValue<bool>() ?? false,
                ["problems"] = problems,
                ["widgets"] = Config?.Widgets.DeepClone() ?? new JsonArray(),
                ["services"] = services,
                ["values"] = _values.Snapshot(),
                ["actions"] = actions,
                ["slots"] = slots,
            };
        }
    }

    /// <summary>Состояние для команды <c>explorer-panel status</c> и <c>check</c>.</summary>
    public StatusResponse BuildStatus()
    {
        lock (_gate)
        {
            var services = _services.ToDictionary(
                pair => pair.Key,
                pair => new ServiceInfo(pair.Value.Current.Status.ToString().ToLowerInvariant(), pair.Value.Current.Detail));
            return new StatusResponse(Root, Trusted, Loaded.Ok, Loaded.Problems.Select(p => p.ToString()).ToList(), services,
                _values.Snapshot(), LastError);
        }
    }

    // ───────── изменения от страницы и снаружи ─────────

    public void SetValue(string name, JsonNode? value)
    {
        // Значение, объявленное в panel.json без команды, хранит программа — оно переживает перезапуск.
        // Необъявленное прислали снаружи: его пришлют снова.
        bool stored;
        lock (_gate) stored = Config?.Values.Any(v => v.Name == name && v.Run == null) == true;
        _values.Set(name, value, persist: stored);
    }

    public void SetCollapsed(bool collapsed)
    {
        lock (_gate)
        {
            if ((_state.Root["collapsed"]?.GetValue<bool>() ?? false) == collapsed) return;
            _state.Root["collapsed"] = collapsed;
            _state.Save();
        }
        RaiseChanged();
    }

    public void SetTrusted(bool? state)
    {
        _trust.Set(Root, state);
        RaiseChanged();
    }

    // ───────── действия ─────────

    // служебные файлы проекта перед первым запуском команды: логи не должны попасть в git
    void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        try
        {
            var gitignore = Path.Combine(Root, PanelLoader.DirName, ".gitignore");
            if (!File.Exists(gitignore)) File.WriteAllText(gitignore, "logs/\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void SetProgress(string action, string? progress)
    {
        lock (_gate)
        {
            if (progress == null) _progress.Remove(action);
            else _progress[action] = progress;
        }
        RaiseChanged();
    }

    async Task<ApiResult> Finish(string action, ApiResult result)
    {
        SetProgress(action, result.Ok ? "ok" : "failed");
        if (!result.Ok) _log.Append(action, "--- failed: " + result.Error);
        _ = Task.Delay(ProgressShownFor).ContinueWith(_ =>
        {
            bool still;
            lock (_gate) still = _progress.GetValueOrDefault(action) is "ok" or "failed";
            if (still) SetProgress(action, null);
        });
        await Task.CompletedTask;
        return result;
    }

    /// <summary>Выполняет встроенное действие (start, stop, restart, logs) или действие из panel.json.</summary>
    /// <param name="parameters">Дополнительные подстановки, например <c>date</c> от календаря.</param>
    public async Task<ApiResult> DoAsync(string action, IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (Trusted != true)
            return new ApiResult(false, "this folder's panel is not allowed yet; allow it in the panel or run: explorer-panel trust");

        PanelConfig? config;
        lock (_gate) config = Config;
        if (config == null) return new ApiResult(false, "panel.json is not loaded; run: explorer-panel check");

        string? Value(string name) => parameters != null && parameters.TryGetValue(name, out var parameter) ? parameter : ValueText(name);

        if (BuiltinActions.TryParse(action, out var verb, out var serviceName))
        {
            if (verb == BuiltinActions.Logs)
            {
                Prepare();
                if (!File.Exists(LogFile)) _log.Append("panel", "--- nothing has been started from the panel yet");
                _shell.OpenInNotepad(LogFile);
                return new ApiResult(true);
            }

            List<ServiceRunner> targets;
            lock (_gate)
                targets = serviceName == null ? _services.Values.ToList() : _services.Values.Where(s => s.Def.Name == serviceName).ToList();
            if (serviceName != null && targets.Count == 0) return new ApiResult(false, $"unknown service \"{serviceName}\"");

            SetProgress(action, "running");
            if (verb is BuiltinActions.Stop or BuiltinActions.Restart) await Task.WhenAll(targets.Select(t => t.StopAsync(Value)));
            string? error = null;
            if (verb is BuiltinActions.Start or BuiltinActions.Restart)
                foreach (var target in targets)
                {
                    var started = await target.StartAsync(Value, Prepare);
                    if (!started.Ok) error ??= $"{target.Def.Name}: {started.Error}";
                }
            return await Finish(action, error == null ? new ApiResult(true) : new ApiResult(false, error));
        }

        var def = config.Actions.FirstOrDefault(a => a.Name == action);
        if (def == null) return new ApiResult(false, $"unknown action \"{action}\"{Suggest.Hint(action, config.Actions.Select(a => a.Name).Concat(BuiltinActions.All))}");

        lock (_gate)
            if (_progress.GetValueOrDefault(action) == "running") return new ApiResult(true); // повторное нажатие, пока выполняется
        SetProgress(action, "running");
        try
        {
            if (await StartNeeded(def, Value) is { } needsError) return await Finish(action, new ApiResult(false, needsError));

            if (def.Open is { } open)
            {
                bool isUrl = Uri.TryCreate(open, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
                var target = isUrl ? Template.FillUrl(open, Value) : Template.Fill(open, Value);
                if (!isUrl && !Path.IsPathFullyQualified(target) && !target.Contains("://")) target = Path.GetFullPath(Path.Combine(Root, target));
                _shell.Open(target);
                return await Finish(action, new ApiResult(true));
            }

            if (!Template.TryFillCommand(def.Run!, Value, out var command, out var error))
                return await Finish(action, new ApiResult(false, error));

            Prepare();
            var cwd = Path.GetFullPath(Path.Combine(Root, def.Cwd ?? "."));
            _log.Append(action, $"--- run: {command}  (in {cwd})");
            var result = await CommandRunner.RunAsync(command, cwd, def.Env, ActionTimeout);
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) _log.Append(action, line.TrimEnd('\r'));
            _log.Append(action, $"--- finished with exit code {result.ExitCode}");
            return await Finish(action, result.ExitCode == 0 ? new ApiResult(true) : new ApiResult(false, $"exit code {result.ExitCode}; see the log: {LogFile}"));
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return await Finish(action, new ApiResult(false, ex.Message));
        }
    }

    // Серверы, которые нужны действию: запускает и ждёт, пока начнут отвечать. Возвращает текст ошибки или null.
    async Task<string?> StartNeeded(ActionDef def, Func<string, string?> value)
    {
        if (def.Needs.Count == 0) return null;
        List<ServiceRunner> needed;
        lock (_gate) needed = _services.Values.Where(s => def.Needs.Contains(s.Def.Name)).ToList();

        foreach (var runner in needed)
        {
            await runner.RefreshAsync(allowCommands: true);
            if (runner.Current.Status == Core.ServiceStatus.Running) continue;
            var started = await runner.StartAsync(value, Prepare);
            if (!started.Ok) return $"service \"{runner.Def.Name}\" did not start: {started.Error}";
        }

        var deadline = DateTime.UtcNow + NeedsTimeout;
        while (true)
        {
            await Task.WhenAll(needed.Select(r => r.RefreshAsync(allowCommands: true)));
            if (needed.All(r => r.Current.Status == Core.ServiceStatus.Running)) return null;
            if (needed.FirstOrDefault(r => r.Current.Status == Core.ServiceStatus.Failed) is { } failed)
                return $"service \"{failed.Def.Name}\" did not start: {failed.Current.Detail ?? "failed"}";
            if (DateTime.UtcNow > deadline) return "services did not start responding in time";
            await Task.Delay(250);
        }
    }

    // ───────── периодическая работа ─────────

    /// <summary>Проверяет серверы и запускает обработчики значений, которым пришло время.</summary>
    public async Task RefreshAsync()
    {
        // Пока папка не разрешена, из неё ничего не выполняется — в том числе запросы по её проверочным
        // адресам: распакованный архив не должен заставлять программу обращаться в сеть.
        if (Trusted != true) return;
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return; // предыдущий обход ещё идёт
        try
        {
            List<ServiceRunner> runners;
            lock (_gate) runners = _services.Values.ToList();
            bool trusted = Trusted == true;
            await Task.WhenAll(runners.Select(r => r.RefreshAsync(allowCommands: trusted)));
            if (_active && trusted) await RunDueHandlers();
        }
        finally { Interlocked.Exchange(ref _refreshing, 0); }
    }

    async Task RunDueHandlers()
    {
        PanelConfig? config;
        List<ValueDef> due;
        lock (_gate)
        {
            config = Config;
            if (config == null) return;
            var now = DateTime.UtcNow;
            due = config.Values.Where(v => v.Run != null && now >= _handlerDue.GetValueOrDefault(v.Name)).ToList();
            foreach (var value in due) _handlerDue[value.Name] = now + TimeSpan.FromSeconds(value.Every);
        }

        await Task.WhenAll(due.Select(async value =>
        {
            if (!Template.TryFillCommand(value.Run!, ValueText, out var command, out var error))
            {
                _log.Append(value.Name, "--- handler skipped: " + error);
                return;
            }
            var cwd = Path.GetFullPath(Path.Combine(Root, value.Cwd ?? "."));
            var result = await CommandRunner.RunAsync(command, cwd, new Dictionary<string, string>(), HandlerTimeout);
            if (result.ExitCode != 0)
            {
                _log.Append(value.Name, $"--- handler failed with exit code {result.ExitCode}: {result.Output.Trim()}");
                return;
            }

            var output = HandlerOutput.Parse(result.Output);
            bool slotsChanged = false;
            lock (_gate)
            {
                if (output.Ttl is { } ttl) _handlerDue[value.Name] = DateTime.UtcNow + TimeSpan.FromSeconds(ttl);
                if (output.Widgets != null)
                {
                    var valid = PanelLoader.ValidWidgets(output.Widgets, config, Root);
                    slotsChanged = !_slots.TryGetValue(value.Name, out var old) || old.ToJsonString() != valid.ToJsonString();
                    _slots[value.Name] = valid;
                }
            }
            _values.Set(value.Name, output.Value, persist: false);
            if (slotsChanged) RaiseChanged();
        }));
    }

    public void Dispose()
    {
        // проект не держит открытых файлов и папок; серверы при закрытии программы продолжают работать
    }
}

/// <summary>Проекты, с которыми сейчас работает программа.</summary>
sealed class ProjectRegistry(TrustStore trust, IShell shell) : IDisposable
{
    readonly Dictionary<string, Project> _projects = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();

    /// <summary>В каком-то проекте что-то изменилось (приходит из любого потока).</summary>
    public event Action<Project>? Changed;

    public IReadOnlyCollection<Project> All
    {
        get { lock (_gate) return _projects.Values.ToList(); }
    }

    /// <summary>Проект, к которому относится папка, или null, если у неё нет панели.</summary>
    public Project? ForFolder(string? folder)
    {
        var root = ProjectLocator.FindRoot(folder);
        if (root == null) return null;
        Project? project;
        bool created = false;
        lock (_gate)
        {
            if (!_projects.TryGetValue(root, out project))
            {
                project = new Project(root, trust, AppPaths.StateFile(root), shell);
                _projects[root] = project;
                created = true;
            }
        }
        if (created) project.Changed += () => Changed?.Invoke(project);
        else project.ReloadIfChanged();
        return project;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var project in _projects.Values) project.Dispose();
            _projects.Clear();
        }
    }
}
