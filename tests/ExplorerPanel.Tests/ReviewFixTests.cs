using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ExplorerPanel.Cli;
using ExplorerPanel.Core;
using ExplorerPanel.Host;
using Microsoft.Win32;

namespace ExplorerPanel.Tests;

// Тесты на находки независимой проверки кода.

public class SafeValueTests
{
    [Theory]
    [InlineData("main\n")]
    [InlineData("main\r\n")]
    [InlineData("a\nb")]
    public void A_value_with_a_line_break_is_not_safe_for_a_command(string value)
    {
        // cmd выполняет только первую строку и сообщает об успехе — остаток команды молча пропал бы
        bool ok = Template.TryFillCommand("git checkout {branch} && echo done", name => value, out _, out var error);

        Assert.False(ok);
        Assert.Contains("\"branch\"", error);
    }

    [Fact]
    public void A_command_in_panel_json_must_be_a_single_line()
    {
        using var project = new TempProject();
        var result = PanelLoader.Parse("""{ "services": { "api": { "run": "echo one\necho two" } } }""", project.Root);

        Assert.False(result.Ok);
        Assert.Contains("single line", Assert.Single(result.Problems, p => p.IsError).Message);
    }
}

public class CommandArgsTests : IDisposable
{
    readonly TempProject _project = new("tests", "web", "sub");
    public void Dispose() => _project.Dispose();

    (string Folder, List<string> Others) Split(string command, params string[] args)
    {
        var folder = Commands.SplitArgs(command, args, _project.Root, out var rest);
        return (folder, rest);
    }

    [Fact]
    public void Do_takes_the_action_even_if_a_folder_has_that_name()
    {
        var (folder, rest) = Split("do", "tests");
        Assert.Equal(_project.Root, folder);
        Assert.Equal(new[] { "tests" }, rest);
    }

    [Fact]
    public void Stop_takes_a_service_name_not_a_folder_of_that_name()
    {
        var (folder, rest) = Split("stop", "web");
        Assert.Equal(_project.Root, folder);
        Assert.Equal(new[] { "web" }, rest);
    }

    [Theory]
    [InlineData(@".\web")]
    [InlineData("./web")]
    [InlineData(".")]
    public void A_path_like_argument_of_stop_is_the_folder(string argument)
    {
        var (folder, rest) = Split("stop", argument);
        Assert.Equal(Path.GetFullPath(Path.Combine(_project.Root, argument)), folder);
        Assert.Empty(rest);
    }

    [Fact]
    public void Second_argument_of_do_is_the_folder()
    {
        var (folder, rest) = Split("do", "tests", "sub");
        Assert.Equal(Path.Combine(_project.Root, "sub"), folder);
        Assert.Equal(new[] { "tests" }, rest);
    }

    [Fact]
    public void Service_and_folder_together()
    {
        var (folder, rest) = Split("restart", "web", "sub");
        Assert.Equal(Path.Combine(_project.Root, "sub"), folder);
        Assert.Equal(new[] { "web" }, rest);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("trust")]
    [InlineData("status")]
    [InlineData("logs")]
    public void Commands_without_arguments_take_the_folder(string command)
    {
        var (folder, rest) = Split(command, "sub");
        Assert.Equal(Path.Combine(_project.Root, "sub"), folder);
        Assert.Empty(rest);

        Assert.Equal(_project.Root, Split(command).Folder);
    }

    [Fact]
    public void Folder_option_wins()
    {
        var (folder, rest) = Split("do", "tests", "--folder", "sub");
        Assert.Equal(Path.Combine(_project.Root, "sub"), folder);
        Assert.Equal(new[] { "tests" }, rest);
    }

    [Fact]
    public void Set_takes_values_and_an_optional_folder()
    {
        var (folder, rest) = Split("set", "a=1", "b=2", "sub");
        Assert.Equal(Path.Combine(_project.Root, "sub"), folder);
        Assert.Equal(new[] { "a=1", "b=2" }, rest);

        Assert.Equal(_project.Root, Split("set", "a=1").Folder);
    }
}

public class InstallPathTests : IDisposable
{
    readonly string _keyName = @"Software\ExplorerPanelsTests\" + Guid.NewGuid().ToString("N")[..8];
    readonly RegistryKey _run, _environment;
    const string Dir = @"C:\Users\me\AppData\Local\Programs\ExplorerPanels";

    public InstallPathTests()
    {
        _run = Registry.CurrentUser.CreateSubKey(_keyName + @"\Run");
        _environment = Registry.CurrentUser.CreateSubKey(_keyName + @"\Environment");
    }

    public void Dispose()
    {
        _run.Dispose();
        _environment.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyName, throwOnMissingSubKey: false);
    }

    string RawPath => (string)_environment.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)!;

    [Fact]
    public void Register_keeps_variables_in_PATH_unexpanded()
    {
        // в PATH пользователя часто лежат записи вида %USERPROFILE%\bin — их нельзя «замораживать» в конкретные пути
        _environment.SetValue("Path", @"%USERPROFILE%\bin;C:\a", RegistryValueKind.ExpandString);

        InstallCommand.Register(Dir, _run, _environment);

        Assert.Equal($@"%USERPROFILE%\bin;C:\a;{Dir}", RawPath);
        Assert.Equal(RegistryValueKind.ExpandString, _environment.GetValueKind("Path"));
        Assert.Equal($"\"{Dir}\\explorer-panel-host.exe\"", _run.GetValue(InstallCommand.RunValueName));
    }

    [Fact]
    public void Register_twice_does_not_duplicate_the_entry()
    {
        _environment.SetValue("Path", @"C:\a", RegistryValueKind.ExpandString);

        InstallCommand.Register(Dir, _run, _environment);
        InstallCommand.Register(Dir.ToUpperInvariant() + "\\", _run, _environment);

        Assert.Equal($@"C:\a;{Dir}", RawPath);
    }

    [Fact]
    public void Register_creates_PATH_when_there_is_none()
    {
        InstallCommand.Register(Dir, _run, _environment);
        Assert.Equal(Dir, RawPath);
    }

    [Fact]
    public void Unregister_removes_the_entry_and_the_autostart()
    {
        _environment.SetValue("Path", @"%USERPROFILE%\bin;C:\a", RegistryValueKind.ExpandString);
        InstallCommand.Register(Dir, _run, _environment);

        InstallCommand.Unregister(Dir, _run, _environment);

        Assert.Equal(@"%USERPROFILE%\bin;C:\a", RawPath);
        Assert.Equal(RegistryValueKind.ExpandString, _environment.GetValueKind("Path"));
        Assert.Null(_run.GetValue(InstallCommand.RunValueName));
    }
}

/// <summary>Находки про проекты и серверы (на настоящих процессах).</summary>
public class ProjectReviewTests : IDisposable
{
    readonly TempProject _dir = new("backend");
    readonly FakeShell _shell = new();
    readonly TrustStore _trust;
    readonly string _data;
    readonly int _port = TestNet.FreePort();
    readonly List<Project> _projects = [];

    public ProjectReviewTests()
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
        foreach (int pid in ProcessInfo.ListeningPids(_port)) ProcessInfo.KillTree(pid);
        _dir.Dispose();
    }

    string Health => $"http://127.0.0.1:{_port}/";

    Project Open(bool trusted = true, string? root = null)
    {
        root ??= _dir.Root;
        if (!File.Exists(PanelLoader.PanelFile(root)))
            _dir.WritePanel($$"""{ "services": { "api": { "run": "python -m http.server {{_port}} --bind 127.0.0.1", "health": "{{Health}}" } } }""");
        if (trusted) _trust.Set(root, true);
        var project = new Project(root, _trust, AppPaths.StateFileIn(_data, root), _shell);
        _projects.Add(project);
        return project;
    }

    static async Task<bool> Reaches(Project project, ServiceStatus wanted, int timeoutMs = 25000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            await project.RefreshAsync();
            if (project.ServiceStatus("api").Status == wanted) return true;
            await Task.Delay(150);
        }
        return false;
    }

    [Fact]
    public void A_visited_project_folder_can_still_be_renamed()
    {
        var project = Open();
        var renamed = _dir.Root + " renamed";

        Directory.Move(_dir.Root, renamed); // раньше слежение за папкой держало её открытой: «Отказано в доступе»

        Assert.True(Directory.Exists(renamed));
        Directory.Move(renamed, _dir.Root);
        GC.KeepAlive(project);
    }

    [Fact]
    public async Task An_untrusted_folder_makes_no_network_requests()
    {
        // «проверочный адрес» из чужой папки — просто слушающий порт; считаем, сколько раз к нему подключились
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        int connections = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref connections);
                }
            }
            catch { }
        });
        try
        {
            var project = Open(trusted: false);
            project.SetActive(true);

            for (int i = 0; i < 3; i++) await project.RefreshAsync();
            await Task.Delay(300);

            Assert.Equal(0, Volatile.Read(ref connections));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Concurrent_starts_spawn_one_server()
    {
        var project = Open();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => project.DoAsync("start"))));

        Assert.True(await Reaches(project, ServiceStatus.Running));
        Assert.Single(ProcessInfo.ListeningPids(_port));
        Assert.Equal(1, File.ReadAllLines(project.LogFile).Count(line => line.Contains("--- start:")));
    }

    [Fact]
    public async Task Concurrent_restarts_leave_one_server_and_stop_really_stops_it()
    {
        var project = Open();

        foreach (int stagger in new[] { 0, 10, 25, 40, 60, 5 })
        {
            // два «перезапустить» почти одновременно (быстрый двойной щелчок)
            var first = Task.Run(() => project.DoAsync("restart"));
            await Task.Delay(stagger);
            var second = Task.Run(() => project.DoAsync("restart"));
            await Task.WhenAll(first, second);

            Assert.True(await Reaches(project, ServiceStatus.Running), $"not running (stagger {stagger} ms)");
            Assert.Single(ProcessInfo.ListeningPids(_port));

            await project.DoAsync("stop");
            Assert.True(await TestNet.Until(() => !TestNet.IsListening(_port), 8000), $"still listening after stop (stagger {stagger} ms)");
            await project.RefreshAsync();
            Assert.Equal(ServiceStatus.Stopped, project.ServiceStatus("api").Status);
        }
    }

    [Fact]
    public async Task A_restart_during_a_slow_stop_does_not_lose_track_of_the_server()
    {
        // у сервера своя команда остановки, которая выполняется около секунды
        _dir.WritePanel($$"""
            { "services": { "api": { "run": "python -m http.server {{_port}} --bind 127.0.0.1", "health": "{{Health}}",
                                     "stop": "ping -n 2 127.0.0.1 >nul" } } }
            """);
        var project = Open();
        await project.DoAsync("start");
        Assert.True(await Reaches(project, ServiceStatus.Running));

        // второй «перезапустить» приходит, пока первый ещё останавливает сервер
        var first = Task.Run(() => project.DoAsync("restart"));
        await Task.Delay(300);
        var second = Task.Run(() => project.DoAsync("restart"));
        await Task.WhenAll(first, second);
        Assert.True(await Reaches(project, ServiceStatus.Running));

        await project.DoAsync("stop");

        Assert.True(await TestNet.Until(() => !TestNet.IsListening(_port), 8000), "a server was left running after Stop");
    }

    [Fact]
    public async Task A_server_started_before_a_host_restart_is_found_and_its_crash_is_seen()
    {
        var first = Open();
        await first.DoAsync("start");
        Assert.True(await Reaches(first, ServiceStatus.Running));

        // «перезапуск программы»: новый объект проекта с тем же файлом состояния
        var second = Open();
        Assert.True(await Reaches(second, ServiceStatus.Running));

        foreach (int pid in ProcessInfo.ListeningPids(_port)) ProcessInfo.KillTree(pid); // сервер упал

        Assert.True(await Reaches(second, ServiceStatus.Failed));
    }
}
