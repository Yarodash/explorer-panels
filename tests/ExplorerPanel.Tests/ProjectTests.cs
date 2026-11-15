using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ExplorerPanel.Core;
using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

/// <summary>Запоминает, что программа попросила открыть, вместо того чтобы открывать на самом деле.</summary>
sealed class FakeShell : IShell
{
    public List<string> Opened { get; } = [];
    public List<string> InNotepad { get; } = [];
    public void Open(string target) => Opened.Add(target);
    public void OpenInNotepad(string file) => InNotepad.Add(file);
}

/// <summary>
/// Проверки на настоящих процессах: сервером служит <c>python -m http.server</c>, поэтому на машине нужен Python.
/// </summary>
public class ProjectTests : IDisposable
{
    readonly TempProject _dir = new("backend");
    readonly FakeShell _shell = new();
    readonly TrustStore _trust;
    readonly string _data;
    readonly int _port = TestNet.FreePort();
    readonly List<Project> _projects = [];
    readonly List<Process> _processes = [];

    public ProjectTests()
    {
        _data = Path.Combine(Path.GetDirectoryName(_dir.Root)!, "data");
        _trust = new TrustStore(Path.Combine(_data, "trust.json"));
    }

    public void Dispose()
    {
        foreach (var project in _projects)
        {
            try { project.DoAsync("stop").Wait(15000); } catch { }
            project.Dispose();
        }
        foreach (var process in _processes)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
        }
        _dir.Dispose();
    }

    string Server => $"python -m http.server {_port} --bind 127.0.0.1";
    string Health => $"http://127.0.0.1:{_port}/";

    Project Open(string json, bool trusted = true)
    {
        _dir.WritePanel(json);
        if (trusted) _trust.Set(_dir.Root, true);
        var project = new Project(_dir.Root, _trust, AppPaths.StateFileIn(_data, _dir.Root), _shell);
        _projects.Add(project);
        return project;
    }

    Project OpenWithServer(bool trusted = true) =>
        Open($$"""{ "services": { "api": { "run": "{{Server}}", "health": "{{Health}}" } } }""", trusted);

    static async Task<bool> Reaches(Project project, string service, ServiceStatus wanted, int timeoutMs = 25000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            await project.RefreshAsync();
            if (project.ServiceStatus(service).Status == wanted) return true;
            await Task.Delay(150);
        }
        return false;
    }

    // ───────── серверы ─────────

    [Fact]
    public async Task Start_then_running_then_stop()
    {
        var project = OpenWithServer();
        Assert.Equal(ServiceStatus.Stopped, project.ServiceStatus("api").Status);

        Assert.True((await project.DoAsync("start")).Ok);
        Assert.True(await Reaches(project, "api", ServiceStatus.Running));
        Assert.True(TestNet.IsListening(_port));

        Assert.True((await project.DoAsync("stop")).Ok);
        Assert.True(await Reaches(project, "api", ServiceStatus.Stopped));
        Assert.False(TestNet.IsListening(_port));
    }

    [Fact]
    public async Task Double_start_spawns_once()
    {
        var project = OpenWithServer();

        await Task.WhenAll(project.DoAsync("start"), project.DoAsync("start"));
        Assert.True(await Reaches(project, "api", ServiceStatus.Running));
        await project.DoAsync("start"); // и третий раз — уже работающему серверу

        Assert.Single(ProcessInfo.ListeningPids(_port));
        var starts = File.ReadAllLines(project.LogFile).Count(line => line.Contains("--- start:"));
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task Untrusted_project_refuses_commands()
    {
        var project = OpenWithServer(trusted: false);

        var result = await project.DoAsync("start");

        Assert.False(result.Ok);
        Assert.Contains("not allowed", result.Error);
        await Task.Delay(700);
        Assert.False(TestNet.IsListening(_port));
        Assert.Null(project.BuildState("en")["trusted"]); // странице уходит «ещё не решено»
    }

    [Fact]
    public async Task Crashed_service_is_failed()
    {
        var project = OpenWithServer();
        await project.DoAsync("start");
        Assert.True(await Reaches(project, "api", ServiceStatus.Running));

        foreach (int pid in ProcessInfo.ListeningPids(_port)) ProcessInfo.KillTree(pid); // сервер упал сам

        Assert.True(await Reaches(project, "api", ServiceStatus.Failed));
    }

    [Fact]
    public async Task Foreign_port_owner_is_failed_and_not_killed()
    {
        // порт занят посторонней программой (здесь — самим процессом тестов, он работает не из папки проекта)
        var foreign = new TcpListener(IPAddress.Loopback, _port);
        foreign.Start();
        try
        {
            var project = OpenWithServer();

            Assert.True(await Reaches(project, "api", ServiceStatus.Failed));
            Assert.Contains("port in use by another program", project.ServiceStatus("api").Detail);

            var start = await project.DoAsync("start");
            Assert.False(start.Ok);
            Assert.Contains("port", start.Error);

            await project.DoAsync("stop");
            Assert.Contains(Environment.ProcessId, ProcessInfo.ListeningPids(_port)); // чужой процесс цел
        }
        finally { foreign.Stop(); }
    }

    [Fact]
    public async Task Server_started_outside_the_panel_from_the_project_folder_is_running_and_stoppable()
    {
        var project = OpenWithServer();
        var outside = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + Server)
        {
            WorkingDirectory = Path.Combine(_dir.Root, "backend"), CreateNoWindow = true, UseShellExecute = false,
        })!;
        _processes.Add(outside);

        Assert.True(await Reaches(project, "api", ServiceStatus.Running));

        await project.DoAsync("stop");
        Assert.True(await Reaches(project, "api", ServiceStatus.Stopped));
        Assert.False(TestNet.IsListening(_port));
    }

    [Fact]
    public async Task Stop_command_is_used_when_present()
    {
        var project = Open($$"""
            { "services": { "api": { "run": "{{Server}}", "health": "{{Health}}", "stop": "echo stopped> stop-marker.txt" } } }
            """);
        await project.DoAsync("start");
        Assert.True(await Reaches(project, "api", ServiceStatus.Running));

        await project.DoAsync("stop");

        Assert.True(File.Exists(Path.Combine(_dir.Root, "stop-marker.txt")));
        Assert.True(await Reaches(project, "api", ServiceStatus.Stopped));
    }

    [Fact]
    public async Task Launcher_that_exits_with_zero_is_not_a_failure()
    {
        // команда-запускалка сразу завершается успешно, а сервер появляется отдельно (как docker compose up -d)
        var project = Open($$"""{ "services": { "api": { "run": "cd .", "health": "{{Health}}", "stop": "cd ." } } }""");

        await project.DoAsync("start");
        await Task.Delay(1500);
        await project.RefreshAsync();

        Assert.Equal(ServiceStatus.Starting, project.ServiceStatus("api").Status);
    }

    // ───────── действия ─────────

    [Fact]
    public async Task Action_with_needs_starts_services_first()
    {
        var project = Open($$"""
            { "services": { "api": { "run": "{{Server}}", "health": "{{Health}}" } },
              "actions": { "site": { "open": "{{Health}}?date={date}", "needs": ["api"] } } }
            """);

        var result = await project.DoAsync("site", new Dictionary<string, string> { ["date"] = "2026-11-15" });

        Assert.True(result.Ok, result.Error);
        Assert.Equal(ServiceStatus.Running, project.ServiceStatus("api").Status);
        Assert.Equal(new[] { Health + "?date=2026-11-15" }, _shell.Opened);
    }

    [Fact]
    public async Task Run_action_reports_failure_and_logs_output()
    {
        var project = Open("""{ "actions": { "broken": { "run": "echo oops & exit 3" } } }""");

        var result = await project.DoAsync("broken");

        Assert.False(result.Ok);
        Assert.Contains("exit code 3", result.Error);
        Assert.Contains("[broken] oops", File.ReadAllText(project.LogFile));
    }

    [Fact]
    public async Task Run_action_uses_cwd()
    {
        var project = Open("""{ "actions": { "mark": { "run": "echo here> marker.txt", "cwd": "backend" } } }""");

        Assert.True((await project.DoAsync("mark")).Ok);

        // файл появился именно в backend, а не в корне проекта
        Assert.True(File.Exists(Path.Combine(_dir.Root, "backend", "marker.txt")));
        Assert.False(File.Exists(Path.Combine(_dir.Root, "marker.txt")));
    }

    [Fact]
    public async Task Unsafe_value_blocks_command()
    {
        var project = Open("""{ "values": { "name": {} }, "actions": { "greet": { "run": "echo {name}> out.txt" } } }""");

        project.SetValue("name", JsonValue.Create("a & echo hacked> hacked.txt"));
        var blocked = await project.DoAsync("greet");

        Assert.False(blocked.Ok);
        Assert.Contains("\"name\"", blocked.Error);
        Assert.False(File.Exists(Path.Combine(_dir.Root, "out.txt")));
        Assert.False(File.Exists(Path.Combine(_dir.Root, "hacked.txt")));

        project.SetValue("name", JsonValue.Create("world"));
        Assert.True((await project.DoAsync("greet")).Ok);
        Assert.Equal("world", File.ReadAllText(Path.Combine(_dir.Root, "out.txt")).Trim());
    }

    [Fact]
    public async Task Logs_action_opens_the_log_in_notepad()
    {
        var project = Open("""{ "actions": { "hello": { "run": "echo hi" } } }""");

        Assert.True((await project.DoAsync("logs")).Ok);

        Assert.Equal(new[] { project.LogFile }, _shell.InNotepad);
        Assert.True(File.Exists(project.LogFile));
    }

    [Fact]
    public async Task Unknown_action_is_an_error()
    {
        var project = Open("{}");
        var result = await project.DoAsync("nope");
        Assert.False(result.Ok);
        Assert.Contains("unknown action", result.Error);
    }

    [Fact]
    public async Task First_command_creates_gitignore_for_logs()
    {
        var project = Open("""{ "actions": { "hello": { "run": "echo hi" } } }""");
        await project.DoAsync("hello");

        var gitignore = Path.Combine(_dir.Root, PanelLoader.DirName, ".gitignore");
        Assert.Contains("logs/", File.ReadAllText(gitignore));

        File.WriteAllText(gitignore, "custom\n"); // свой файл владельца программа не трогает
        await project.DoAsync("hello");
        Assert.Equal("custom\n", File.ReadAllText(gitignore));
    }

    // ───────── файл панели ─────────

    [Fact]
    public void Broken_json_keeps_last_good_config()
    {
        var project = Open("""{ "widgets": [ { "type": "text", "text": "first" } ] }""");
        Assert.Equal("first", (string?)project.BuildState("en")["widgets"]![0]!["text"]);

        _dir.WritePanel("""{ "widgets": [ { "type": "text", "text": "seco"""); // сохранили половину файла
        project.Reload();

        var state = project.BuildState("en");
        Assert.Equal("first", (string?)state["widgets"]![0]!["text"]);
        Assert.NotEmpty(state["problems"]!.AsArray());
        Assert.False(project.Loaded.Ok);
    }

    [Fact]
    public void Fixed_json_reloads()
    {
        var project = Open("""{ "widgets": [ { "type": "text", "text": "first" } ] }""");
        _dir.WritePanel("{ broken");
        project.Reload();

        _dir.WritePanel("""{ "widgets": [ { "type": "text", "text": "second" } ] }""");
        project.Reload();

        var state = project.BuildState("en");
        Assert.Equal("second", (string?)state["widgets"]![0]!["text"]);
        Assert.Empty(state["problems"]!.AsArray());
    }

    [Fact]
    public void Project_without_a_valid_file_has_no_config()
    {
        var project = Open("{ broken");
        Assert.Null(project.Config);
    }

    // ───────── значения ─────────

    [Fact]
    public async Task Handler_value_is_refreshed_while_the_panel_is_visible()
    {
        var project = Open("""{ "values": { "branch": { "run": "echo main", "every": 1 } } }""");

        await project.RefreshAsync();
        Assert.Null(project.BuildState("en")["values"]!["branch"]); // панели не видно — обработчики не запускаются

        project.SetActive(true);
        Assert.True(await TestNet.Until(() =>
        {
            project.RefreshAsync().Wait();
            return (string?)project.BuildState("en")["values"]!["branch"] == "main";
        }));
    }

    [Fact]
    public async Task Handler_envelope_sets_value_and_slot_widgets()
    {
        _dir.WritePanel("{}");
        File.WriteAllText(Path.Combine(_dir.Root, "handler.py"),
            """print('{"value": "3 new", "ttl": 120, "widgets": [{"type": "text", "text": "3 new commits"}, {"type": "hologram"}]}')""");
        var project = Open("""{ "values": { "commits": { "run": "python handler.py", "every": 1 } } }""");
        project.SetActive(true);

        Assert.True(await TestNet.Until(() =>
        {
            project.RefreshAsync().Wait();
            return project.BuildState("en")["slots"]!["commits"] != null;
        }));

        var state = project.BuildState("en");
        Assert.Equal("3 new", (string?)state["values"]!["commits"]);
        var widget = Assert.Single(state["slots"]!["commits"]!.AsArray()); // виджет неизвестного типа отброшен
        Assert.Equal("3 new commits", (string?)widget!["text"]);
    }

    [Fact]
    public void Stored_value_has_default_and_survives_restart()
    {
        const string json = """{ "values": { "env": { "default": "dev" } } }""";
        var project = Open(json);
        Assert.Equal("dev", (string?)project.BuildState("en")["values"]!["env"]);

        project.SetValue("env", JsonValue.Create("prod"));
        project.SetCollapsed(true);

        var reopened = Open(json);
        var state = reopened.BuildState("en");
        Assert.Equal("prod", (string?)state["values"]!["env"]);
        Assert.True((bool)state["collapsed"]!);
    }

    [Fact]
    public void State_has_the_shape_the_page_expects()
    {
        var project = Open($$"""{ "services": { "api": { "run": "{{Server}}", "health": "{{Health}}" } } }""");

        var state = project.BuildState("ru");

        Assert.Equal(_dir.Root, (string?)state["root"]);
        Assert.Equal("ru", (string?)state["locale"]);
        Assert.True((bool)state["trusted"]!);
        Assert.False((bool)state["collapsed"]!);
        Assert.Equal("stopped", (string?)state["services"]!["api"]!["status"]);
        foreach (var key in new[] { "problems", "widgets", "values", "actions", "slots" }) Assert.NotNull(state[key]);
    }

    [Fact]
    public void Changed_fires_when_a_value_is_set()
    {
        var project = Open("{}");
        int changes = 0;
        project.Changed += () => changes++;

        project.SetValue("deploy", JsonValue.Create("running"));

        Assert.Equal(1, changes);
        Assert.Equal("running", (string?)project.BuildState("en")["values"]!["deploy"]);
    }
}
