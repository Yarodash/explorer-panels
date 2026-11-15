using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ExplorerPanel.Core;
using ExplorerPanel.Core.Api;

namespace ExplorerPanel.Host;

/// <summary>
/// Один сервер проекта: запуск, остановка, проверка «жив ли» и вычисление статуса.
/// Сам статус решает <see cref="ServiceStatusRules"/>; здесь собираются наблюдения для него.
/// </summary>
sealed class ServiceRunner
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };
    static readonly TimeSpan RunningCheckEvery = TimeSpan.FromSeconds(4); // работающий сервер проверяем реже: каждая проверка — строка в его логе
    static readonly TimeSpan CommandCheckEvery = TimeSpan.FromSeconds(3);

    readonly string _root;
    readonly RotatingLog _log;
    readonly StateFile _state;
    readonly object _gate = new();

    // Запуск и остановка выполняются строго по очереди. Иначе быстрый двойной щелчок по «перезапустить»
    // мог оставить процесс без присмотра: остановка, начатая раньше, «забывала» процесс, запущенный позже.
    readonly SemaphoreSlim _operation = new(1, 1);

    // процесс, который запустила панель: номер и время запуска (по ним он узнаётся и после перезапуска программы)
    int _pid;
    long _pidStartTicks;
    bool _watchingExit;    // у нас есть событие о завершении этого процесса (после перезапуска программы его нет)

    bool _wantRunning;     // нажали «запустить» и с тех пор не нажимали «выключить»
    int? _exitCode;        // с каким кодом завершился запущенный панелью процесс
    DateTime _exitedAt;
    bool _healthy;         // последняя проверка «жив ли» прошла
    int _misses;           // сколько проверок подряд не прошло
    DateTime _nextCheck;   // раньше этого времени не проверяем
    string? _startError;   // почему не удалось запустить
    PortOwner _owner;
    string? _foreignName;
    int? _foreignPid;

    public ServiceDef Def { get; private set; }
    public (ServiceStatus Status, string? Detail) Current { get; private set; } = (ServiceStatus.Stopped, null);
    public event Action? Changed;

    public ServiceRunner(string projectRoot, ServiceDef def, RotatingLog log, StateFile state)
    {
        _root = projectRoot;
        Def = def;
        _log = log;
        _state = state;

        // сервер мог быть запущен панелью до перезапуска программы
        var (pid, started) = state.Read(root => StateFile.Section(root, "processes")[def.Name] is JsonObject saved
            ? (saved["pid"]?.GetValue<int>() ?? 0, saved["started"]?.GetValue<long>() ?? 0)
            : (0, 0L));
        if (pid != 0 && ProcessInfo.StartTime(pid)?.Ticks == started)
        {
            _pid = pid;
            _pidStartTicks = started;
            _wantRunning = true;
        }
    }

    /// <summary>Описание сервера в panel.json изменилось; запущенный процесс при этом остаётся под присмотром.</summary>
    public void UpdateDef(ServiceDef def)
    {
        lock (_gate)
        {
            if (def == Def) return;
            Def = def;
            _nextCheck = default;
        }
    }

    string Cwd => Path.GetFullPath(Path.Combine(_root, Def.Cwd ?? "."));

    /// <summary>Процесс, запущенный панелью, ещё работает.</summary>
    public bool OwnAlive()
    {
        lock (_gate) return _pid != 0 && ProcessInfo.StartTime(_pid)?.Ticks == _pidStartTicks;
    }

    // ───────── запуск и остановка ─────────

    /// <param name="values">Значения для подстановки <c>{имя}</c> в команду.</param>
    /// <param name="prepare">Вызывается перед первым запуском команды (создаёт служебные файлы проекта).</param>
    public async Task<ApiResult> StartAsync(Func<string, string?> values, Action prepare)
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try { return Start(values, prepare); }
        finally { _operation.Release(); }
    }

    ApiResult Start(Func<string, string?> values, Action prepare)
    {
        ApiResult result = new(true);
        lock (_gate)
        {
            if (OwnAlive()) return result; // уже запущен панелью — второй раз не запускаем

            ApplyOwner(ResolvePortOwner());
            if (_owner == PortOwner.Project) return result; // уже работает: запущен из папки проекта не панелью
            if (_owner == PortOwner.Foreign)
            {
                result = new ApiResult(false, $"port in use by another program: {_foreignName} ({_foreignPid})");
            }
            else if (!Template.TryFillCommand(Def.Run, values, out var command, out var error))
            {
                _startError = error;
                _log.Append(Def.Name, "--- cannot start: " + error);
                result = new ApiResult(false, error);
            }
            else
            {
                _startError = null;
                _exitCode = null;
                _wantRunning = true;
                _healthy = false;
                _misses = 0;
                _nextCheck = default;
                try
                {
                    prepare();
                    _log.Append(Def.Name, $"--- start: {command}  (in {Cwd})");
                    var process = Pump.Start(command, Cwd, Def.Env, _log.File, Def.Name);
                    _pid = process.Id;
                    try { _pidStartTicks = process.StartTime.ToUniversalTime().Ticks; }
                    catch (InvalidOperationException) { _pidStartTicks = 0; } // уже завершился
                    _watchingExit = true;
                    SaveTracked();
                    process.Exited += (_, _) => OnExited(process);
                    if (process.HasExited) OnExited(process);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
                {
                    _startError = "cannot start: " + ex.Message;
                    _log.Append(Def.Name, "--- " + _startError);
                    result = new ApiResult(false, _startError);
                }
            }
        }
        Recompute();
        return result;
    }

    void OnExited(Process process)
    {
        int code;
        try { code = process.ExitCode; }
        catch (InvalidOperationException) { code = -1; }

        lock (_gate)
        {
            if (process.Id != _pid || _exitCode != null) return; // это событие от прежнего запуска или уже учтено
            NoteExit(code);
        }
        Recompute();
    }

    // вызывается под _gate
    void NoteExit(int code)
    {
        _exitCode = code;
        _exitedAt = DateTime.UtcNow;
        if (code != 0) _healthy = false; // упавший сервер не «работает» ни секунды дольше
        _misses = 1;                     // и одной неудачной проверки теперь достаточно
        _nextCheck = default;
    }

    public async Task StopAsync(Func<string, string?> values)
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try { await Stop(values).ConfigureAwait(false); }
        finally { _operation.Release(); }
    }

    async Task Stop(Func<string, string?> values)
    {
        lock (_gate)
        {
            _wantRunning = false;
            _startError = null;
        }

        if (Def.Stop is { } stop)
        {
            // у сервера своя команда остановки (например, docker compose down)
            if (Template.TryFillCommand(stop, values, out var command, out var error))
            {
                _log.Append(Def.Name, "--- stop: " + command);
                await CommandRunner.RunAsync(command, Cwd, Def.Env, TimeSpan.FromMinutes(2), line => _log.Append(Def.Name, line)).ConfigureAwait(false);
            }
            else _log.Append(Def.Name, "--- cannot run the stop command: " + error);
        }

        // Номер процесса берём только сейчас и проверяем время его запуска: пока шла команда остановки,
        // процесс мог завершиться, а его номер — достаться постороннему процессу.
        int pid;
        lock (_gate) pid = OwnAlive() ? _pid : 0;
        if (pid != 0)
        {
            _log.Append(Def.Name, "--- stopping");
            ProcessInfo.KillTree(pid);
        }

        if (Def.Stop == null)
        {
            // сервер этого проекта, запущенный не панелью (из терминала): он слушает порт и работает из папки проекта
            foreach (int other in ResolvePortOwner().ProjectPids)
            {
                if (!ProcessInfo.IsAlive(other)) continue;
                _log.Append(Def.Name, $"--- stopping process {other} of this project that listens on the port");
                ProcessInfo.KillTree(other);
            }
        }

        lock (_gate)
        {
            _pid = 0;
            _pidStartTicks = 0;
            _watchingExit = false;
            _exitCode = null;
            _healthy = false;
            _misses = 0;
            _nextCheck = default;
            _owner = PortOwner.None;
            SaveTracked();
        }
        Recompute();
    }

    void SaveTracked()
    {
        int pid = _pid;
        long started = _pidStartTicks;
        _state.Update(root =>
        {
            var processes = StateFile.Section(root, "processes");
            if (pid != 0) processes[Def.Name] = new JsonObject { ["pid"] = pid, ["started"] = started };
            else processes.Remove(Def.Name);
        });
    }

    // ───────── наблюдение ─────────

    /// <summary>Проверяет, жив ли сервер и кто слушает его порт, и пересчитывает статус.</summary>
    /// <param name="allowCommands">Можно ли выполнять проверочную команду (только для разрешённой папки).</param>
    public async Task RefreshAsync(bool allowCommands)
    {
        bool due;
        lock (_gate) due = DateTime.UtcNow >= _nextCheck;
        if (due)
        {
            bool? healthy = null;
            if (Def.Health?.Url is { } url) healthy = await RespondsAsync(url).ConfigureAwait(false);
            else if (Def.Health?.Run is { } command && allowCommands)
                healthy = (await CommandRunner.RunAsync(command, Cwd, Def.Env, TimeSpan.FromSeconds(15)).ConfigureAwait(false)).ExitCode == 0;
            var owner = ResolvePortOwner();

            lock (_gate)
            {
                ApplyOwner(owner);
                if (healthy == true)
                {
                    _healthy = true;
                    _misses = 0;
                    _nextCheck = DateTime.UtcNow + RunningCheckEvery;
                }
                else if (healthy == false)
                {
                    // Один пропущенный ответ работающего сервера — ещё не повод показывать «не запущен»:
                    // иначе статус мигал бы при любой заминке. Но если наш процесс умер — верим сразу.
                    _misses++;
                    if (_misses >= 2 || (_pid != 0 && !OwnAlive())) _healthy = false;
                    _nextCheck = Def.Health?.Run != null ? DateTime.UtcNow + CommandCheckEvery : default;
                }
            }
        }
        Recompute();
    }

    void Recompute()
    {
        lock (_gate)
        {
            bool own = OwnAlive();
            // Сервер, запущенный до перезапуска программы, не сообщит о своём завершении событием —
            // замечаем его исчезновение сами, иначе упавший сервер показывался бы как «не запущен».
            if (!own && _pid != 0 && !_watchingExit && _exitCode == null) NoteExit(-1);

            var observation = new ServiceObservation(
                HasHealth: Def.Health != null, Healthy: _healthy, OwnProcessAlive: own,
                ExitCode: own ? null : _exitCode, SecondsSinceExit: own ? 0 : (DateTime.UtcNow - _exitedAt).TotalSeconds,
                WantRunning: _wantRunning, PortOwner: _owner, ForeignName: _foreignName, ForeignPid: _foreignPid);
            var next = _startError != null && !own && !_healthy
                ? (ServiceStatus.Failed, _startError)
                : ServiceStatusRules.Compute(observation);
            if (next == Current) return;
            Current = next;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Отвечает ли сервер по адресу. "localhost" — это сразу два адреса (IPv4 и IPv6), а сервер обычно слушает один;
    /// обращение ко второму на Windows «висит» около двух секунд. Поэтому пробуем оба сразу.
    /// </summary>
    static async Task<bool> RespondsAsync(string url)
    {
        var uri = new Uri(url);
        var candidates = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? new[] { new UriBuilder(uri) { Host = "127.0.0.1" }.Uri, new UriBuilder(uri) { Host = "[::1]" }.Uri }
            : [uri];
        var attempts = candidates.Select(async candidate =>
        {
            try
            {
                using var response = await Http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                return (int)response.StatusCode < 500;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { return false; }
        }).ToList();
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts).ConfigureAwait(false);
            if (await done.ConfigureAwait(false)) return true;
            attempts.Remove(done);
        }
        return false;
    }

    // ───────── чей процесс слушает порт ─────────

    void ApplyOwner((PortOwner Owner, string? Name, int? Pid, List<int> ProjectPids) owner)
    {
        _owner = owner.Owner;
        _foreignName = owner.Name;
        _foreignPid = owner.Pid;
    }

    /// <summary>
    /// Кто слушает порт из проверочного адреса. Имеет смысл только для местного адреса и только если у сервера
    /// нет своей команды остановки: такие серверы (docker compose и подобные) слушают порт через посредника.
    /// </summary>
    (PortOwner Owner, string? Name, int? Pid, List<int> ProjectPids) ResolvePortOwner()
    {
        if (Def.Stop != null || Def.Health?.Url is not { } url
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            return (PortOwner.None, null, null, []);

        var mine = new List<int>();
        int? foreign = null;
        foreach (int pid in ProcessInfo.ListeningPids(uri.Port))
        {
            if (BelongsToProject(pid)) mine.Add(pid);
            // порт сервера в Docker или WSL слушает их посредник: это не посторонняя программа, занявшая порт
            else if (!ServiceStatusRules.IsPortForwarder(ProcessInfo.Name(pid))) foreign ??= pid;
        }
        if (mine.Count > 0) return (PortOwner.Project, null, null, mine);
        if (foreign is { } foreignPid) return (PortOwner.Foreign, ProcessInfo.Name(foreignPid), foreignPid, []);
        return (PortOwner.None, null, null, []);
    }

    // Процесс принадлежит проекту, если его запустила панель (он сам или его предок — наш процесс)
    // либо он работает из папки внутри проекта (сервер, запущенный руками из терминала).
    bool BelongsToProject(int pid)
    {
        int own;
        lock (_gate) own = OwnAlive() ? _pid : 0;
        if (own != 0 && (pid == own || ProcessInfo.ParentChain(pid, 12).Contains(own))) return true;
        var cwd = ProcessInfo.WorkingDirectory(pid);
        return cwd != null && ProjectLocator.Applies(_root, cwd, "tree");
    }
}

/// <summary>
/// Посредник между сервером и его логом — отдельный процесс (эта же программа с ключом <c>--pump</c>).
///
/// Зачем он нужен: вывод сервера кто-то должен читать. Если бы его читала сама фоновая программа, то при её
/// закрытии (обновление, выход из трея, сбой) сервер потерял бы, куда писать, и перестал нормально работать.
/// Посредник живёт столько же, сколько сервер, и от фоновой программы не зависит.
/// </summary>
static class Pump
{
    public const string Switch = "--pump";

    static string Exe => Path.Combine(AppContext.BaseDirectory, "explorer-panel-host.exe");

    /// <summary>Запускает команду под присмотром посредника и возвращает процесс посредника.</summary>
    public static Process Start(string command, string cwd, IReadOnlyDictionary<string, string> env, string logFile, string name)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = cwd };
        info.ArgumentList.Add(Switch);
        info.ArgumentList.Add(logFile);
        info.ArgumentList.Add(name);
        info.ArgumentList.Add(command);
        foreach (var (key, value) in env) info.Environment[key] = value; // посредник передаст окружение серверу
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Start();
        return process;
    }

    /// <summary>
    /// Если программа запущена как посредник — выполняет команду, пишет её вывод в лог и возвращает true.
    /// Код возврата посредника равен коду возврата команды.
    /// </summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 4 || args[0] != Switch) return false;

        var log = new RotatingLog(args[1]);
        string name = args[2], command = args[3];
        try
        {
            var (process, output) = CommandRunner.StartCore(command, Environment.CurrentDirectory,
                new Dictionary<string, string>(), line => log.Append(name, line));
            using (process)
            {
                process.WaitForExit();
                output.Wait(2000);
                exitCode = process.ExitCode;
            }
            log.Append(name, $"--- exited with code {exitCode}");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            log.Append(name, "--- cannot start: " + ex.Message);
            exitCode = 1;
        }
        return true;
    }
}
