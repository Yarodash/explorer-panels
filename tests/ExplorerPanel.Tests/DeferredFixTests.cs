using System.Diagnostics;
using System.Text.Json.Nodes;
using ExplorerPanel.Core;
using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

// Мелочи, отложенные после независимой проверки кода.

public class LiteralBracesTests
{
    static Func<string, string?> Values(params (string Name, string Value)[] pairs) =>
        name => pairs.Where(p => p.Name == name).Select(p => p.Value).FirstOrDefault();

    [Fact]
    public void Doubled_braces_are_literal_in_a_command()
    {
        // git понимает @{u} как «ветка на сервере»; без способа написать скобки буквально такую команду не выразить
        bool ok = Template.TryFillCommand("git rev-list @{{u}}..HEAD --count", Values(), out var command, out var error);

        Assert.True(ok, error);
        Assert.Equal("git rev-list @{u}..HEAD --count", command);
    }

    [Fact]
    public void Doubled_braces_are_literal_in_text_and_addresses()
    {
        Assert.Equal("{x} = 1", Template.Fill("{{x}} = {x}", Values(("x", "1"))));
        Assert.Equal("http://h/{a}/b%20c", Template.FillUrl("http://h/{{a}}/{a}", Values(("a", "b c"))));
    }

    [Fact]
    public void A_value_can_stand_inside_literal_braces()
    {
        Assert.Equal("{main}", Template.Fill("{{{branch}}}", Values(("branch", "main"))));
    }

    [Fact]
    public void Literal_braces_are_not_names()
    {
        Assert.Equal(new[] { "branch" }, Template.Names("@{{u}} {branch} {{{branch}}}"));
    }

    [Fact]
    public void The_loader_does_not_take_a_literal_for_an_undeclared_value()
    {
        using var project = new TempProject();
        var result = PanelLoader.Parse("""{ "actions": { "ahead": { "run": "git rev-list @{{u}}..HEAD --count" } } }""", project.Root);

        Assert.True(result.Ok);
        Assert.Empty(result.Problems);
    }
}

public class OptionLikeValueTests
{
    static bool Fill(string command, string value, out string result, out string error) =>
        Template.TryFillCommand(command, _ => value, out result, out error);

    [Theory]
    [InlineData("git checkout {branch}", "-f")]
    [InlineData("git checkout {branch}", "--force")]
    [InlineData("del {branch}", "/s")]
    [InlineData("{branch} --version", "-x")]
    [InlineData("git checkout \"{branch}\"", "-f")]
    public void A_value_the_command_would_read_as_an_option_is_refused(string command, string value)
    {
        // значение приходит снаружи (имя ветки, присланный текст) и не должно превращаться в ключ команды
        Assert.False(Fill(command, value, out _, out var error));
        Assert.Contains("\"branch\"", error);
        Assert.Contains("option", error);
    }

    [Theory]
    [InlineData("npm run build -- --mode={mode}", "-x", "npm run build -- --mode=-x")]
    [InlineData("curl http://localhost/{mode}", "/a/b", "curl http://localhost//a/b")]
    [InlineData("git checkout {mode}", "feature/a-b", "git checkout feature/a-b")]
    [InlineData("echo {mode}", "2026-11-15", "echo 2026-11-15")]
    public void Dashes_and_slashes_elsewhere_are_fine(string command, string value, string expected)
    {
        Assert.True(Fill(command, value, out var result, out var error), error);
        Assert.Equal(expected, result);
    }
}

public class OpenTargetTests : IDisposable
{
    readonly TempProject _project = new("reports");
    public void Dispose() => _project.Dispose();

    static Func<string, string?> Values(params (string Name, string Value)[] pairs) =>
        name => pairs.Where(p => p.Name == name).Select(p => p.Value).FirstOrDefault();

    bool Resolve(string open, Func<string, string?> values, out string target, out string error) =>
        OpenTarget.TryResolve(open, _project.Root, values, out target, out error);

    [Fact]
    public void A_value_in_a_web_address_is_encoded()
    {
        Assert.True(Resolve("http://localhost:5173/{page}", Values(("page", "a b&c")), out var target, out _));
        Assert.Equal("http://localhost:5173/a%20b%26c", target);
    }

    [Fact]
    public void A_value_in_an_address_of_any_other_program_is_encoded_too()
    {
        // раньше кодировались только http-адреса; в остальные значение вставлялось как есть
        Assert.True(Resolve("vscode://file/{file}", Values(("file", "a b?x=1")), out var target, out _));
        Assert.Equal("vscode://file/a%20b%3Fx%3D1", target);
    }

    [Fact]
    public void A_path_is_relative_to_the_project()
    {
        Assert.True(Resolve("dist", Values(), out var target, out _));
        Assert.Equal(Path.Combine(_project.Root, "dist"), target);
    }

    [Fact]
    public void A_value_can_choose_a_file_inside_the_project()
    {
        Assert.True(Resolve("reports/{day}.html", Values(("day", "2026-11-15")), out var target, out var error), error);
        Assert.Equal(Path.Combine(_project.Root, "reports", "2026-11-15.html"), target);
    }

    [Theory]
    [InlineData("{file}", @"C:\Windows\System32\calc.exe")]
    [InlineData("reports/{file}", @"..\..\secret.txt")]
    [InlineData("{file}", @"\\server\share\run.cmd")]
    [InlineData("{file}", "https://example.com/x")]
    [InlineData("{file}", "shell:startup")]
    public void A_value_cannot_lead_outside_the_project(string open, string value)
    {
        // значение может прислать любая программа: оно не должно открывать произвольный файл или адрес
        Assert.False(Resolve(open, Values(("file", value)), out _, out var error));
        Assert.Contains("outside the project folder", error);
    }

    [Fact]
    public void A_path_with_a_missing_value_is_not_opened()
    {
        Assert.False(Resolve("reports/{day}.html", Values(), out _, out var error));
        Assert.Contains("\"day\" is not set", error);
    }

    [Fact]
    public void What_the_author_wrote_without_values_is_opened_as_written()
    {
        Assert.True(Resolve(@"C:\Windows", Values(), out var target, out _));
        Assert.Equal(@"C:\Windows", target);
    }
}

public class PortForwarderTests
{
    [Theory]
    [InlineData("wslrelay.exe")]
    [InlineData("wslhost.exe")]
    [InlineData("com.docker.backend.exe")]
    [InlineData("COM.DOCKER.BACKEND")]
    [InlineData("docker-proxy")]
    [InlineData("vpnkit.exe")]
    public void Docker_and_WSL_listen_on_behalf_of_someone_else(string name)
    {
        // порт сервера в Docker или WSL слушает их посредник — это не «чужая программа заняла порт»
        Assert.True(ServiceStatusRules.IsPortForwarder(name));
    }

    [Theory]
    [InlineData("node.exe")]
    [InlineData("python.exe")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void Ordinary_programs_do_not(string? name)
    {
        Assert.False(ServiceStatusRules.IsPortForwarder(name));
    }
}

public class StateFileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    string File => Path.Combine(_dir, "state.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Changes_from_several_threads_all_reach_the_file()
    {
        // состояние проекта меняют разные потоки: значения, свёрнутость панели, запущенные процессы
        var state = new StateFile(File);

        Parallel.For(0, 8, thread =>
        {
            for (int i = 0; i < 120; i++)
                state.Update(root =>
                {
                    if (root[$"s{thread}"] is not JsonObject section) root[$"s{thread}"] = section = [];
                    section[$"k{i}"] = i;
                });
        });

        var saved = JsonNode.Parse(System.IO.File.ReadAllText(File))!.AsObject();
        for (int thread = 0; thread < 8; thread++) Assert.Equal(120, saved[$"s{thread}"]!.AsObject().Count);
    }

    [Fact]
    public void What_is_read_is_a_copy()
    {
        var state = new StateFile(File);
        state.Update(root => root["values"] = new JsonObject { ["a"] = 1 });

        var copy = state.Read(root => root["values"]!.DeepClone().AsObject());
        copy["a"] = 2;

        Assert.Equal(1, state.Read(root => root["values"]!["a"]!.GetValue<int>()));
        Assert.Equal(1, new StateFile(File).Read(root => root["values"]!["a"]!.GetValue<int>()));
    }
}

public class FolderProbeTests
{
    [Fact]
    public async Task A_folder_that_does_not_answer_does_not_hold_the_caller()
    {
        // зависшая сетевая папка: обращение к ней длится десятки секунд; окна панелей при этом должны жить
        using var release = new ManualResetEventSlim();
        int looks = 0;
        var probe = new FolderProbe<string>(folder =>
        {
            Interlocked.Increment(ref looks);
            release.Wait();
            return "found " + folder;
        });
        var updated = new SemaphoreSlim(0);
        probe.Updated += () => updated.Release();

        var watch = Stopwatch.StartNew();
        var first = probe.Get(@"\\slow\share");
        var second = probe.Get(@"\\slow\share");
        watch.Stop();

        Assert.False(first.Known);
        Assert.False(second.Known);
        Assert.True(watch.ElapsedMilliseconds < 500, $"the caller waited {watch.ElapsedMilliseconds} ms");
        Assert.True(await TestNet.Until(() => Volatile.Read(ref looks) == 1, 3000));
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref looks)); // второй запрос к той же папке не начинается, пока идёт первый

        release.Set();
        Assert.True(await updated.WaitAsync(3000), "no notification when the answer came");
        var later = probe.Get(@"\\slow\share");
        Assert.True(later.Known);
        Assert.Equal(@"found \\slow\share", later.Value);
    }

    [Fact]
    public async Task Each_request_refreshes_the_answer_and_reports_only_changes()
    {
        string answer = "one";
        var probe = new FolderProbe<string>(_ => answer);
        int updates = 0;
        probe.Updated += () => Interlocked.Increment(ref updates);

        probe.Get(@"C:\a");
        Assert.True(await TestNet.Until(() => probe.Get(@"C:\a") is { Known: true, Value: "one" }, 3000));
        await Task.Delay(150);
        int afterFirst = Volatile.Read(ref updates);
        Assert.Equal(1, afterFirst); // повторные запросы с тем же ответом не сообщают об изменении

        answer = "two";
        Assert.True(await TestNet.Until(() => probe.Get(@"C:\a").Value == "two", 3000));
        Assert.True(await TestNet.Until(() => Volatile.Read(ref updates) == 2, 3000));
    }

    [Fact]
    public async Task A_failing_lookup_is_an_unknown_answer_not_a_crash()
    {
        var probe = new FolderProbe<string>(_ => throw new IOException("the network path was not found"));

        probe.Get(@"\\gone\share");
        await Task.Delay(200);

        Assert.False(probe.Get(@"\\gone\share").Known);
    }
}

public class RetryTests
{
    [Fact]
    public async Task Tries_again_until_it_works()
    {
        int attempts = 0;
        var failures = new List<string>();

        await Retry.UntilDone(() =>
        {
            if (++attempts < 3) throw new InvalidOperationException("engine is not ready");
            return Task.CompletedTask;
        }, stop: () => false, delays: [TimeSpan.FromMilliseconds(5)], onFailure: (ex, _) => failures.Add(ex.Message));

        Assert.Equal(3, attempts);
        Assert.Equal(new[] { "engine is not ready", "engine is not ready" }, failures);
    }

    [Fact]
    public async Task Gives_up_when_told_to_stop()
    {
        int attempts = 0;

        await Retry.UntilDone(() =>
        {
            attempts++;
            throw new InvalidOperationException("never works");
        }, stop: () => attempts >= 2, delays: [TimeSpan.FromMilliseconds(5)], onFailure: (_, _) => { });

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Waits_longer_each_time_up_to_the_last_delay()
    {
        var waits = new List<TimeSpan>();
        int attempts = 0;

        await Retry.UntilDone(() =>
        {
            if (++attempts < 5) throw new InvalidOperationException("no");
            return Task.CompletedTask;
        }, stop: () => false, delays: [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(3)],
            onFailure: (_, wait) => waits.Add(wait));

        Assert.Equal(new[] { 1.0, 2.0, 3.0, 3.0 }, waits.Select(w => w.TotalMilliseconds));
    }
}

/// <summary>Мелочи про проекты и серверы (на настоящих процессах).</summary>
public class ProjectDeferredTests : IDisposable
{
    readonly TempProject _dir = new();
    readonly FakeShell _shell = new();
    readonly TrustStore _trust;
    readonly string _data;
    readonly int _port = TestNet.FreePort();
    readonly List<Project> _projects = [];

    public ProjectDeferredTests()
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

    Project Open(string panelJson)
    {
        _dir.WritePanel(panelJson);
        _trust.Set(_dir.Root, true);
        var project = new Project(_dir.Root, _trust, AppPaths.StateFileIn(_data, _dir.Root), _shell);
        _projects.Add(project);
        return project;
    }

    static string ReadShared(string file)
    {
        if (!File.Exists(file)) return "";
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task An_actions_output_reaches_the_log_while_it_is_still_running()
    {
        // долгое действие (сборка, тесты): по кнопке «логи» должно быть видно, что оно делает прямо сейчас
        var project = Open("""{ "actions": { "slow": { "run": "python -u -c \"import time; print('out' + 'put-1'); time.sleep(6); print('out' + 'put-2')\"" } } }""");

        var running = Task.Run(() => project.DoAsync("slow"));

        Assert.True(await TestNet.Until(() => ReadShared(project.LogFile).Contains("output-1"), 5000), "the first line of output is not in the log yet");
        Assert.False(running.IsCompleted);
        Assert.True((await running).Ok);
        Assert.Contains("output-2", ReadShared(project.LogFile));
    }

    [Fact]
    public async Task A_running_service_removed_from_panel_json_is_stopped()
    {
        var health = $"http://127.0.0.1:{_port}/";
        var project = Open($$"""{ "services": { "api": { "run": "python -m http.server {{_port}} --bind 127.0.0.1" } } }""");
        await project.DoAsync("start");
        Assert.True(await TestNet.Until(() => TestNet.IsListening(_port), 20000), "the server did not start: " + health);

        // сервер убрали (или переименовали) в panel.json: панель его запустила — панель его и останавливает
        _dir.WritePanel("""{ "actions": { "noop": { "run": "echo ok" } } }""");
        project.Reload();

        Assert.True(await TestNet.Until(() => !TestNet.IsListening(_port), 10000), "the removed service keeps running with nobody watching it");
        Assert.Contains("removed from panel.json", ReadShared(project.LogFile));
    }

    [Fact]
    public async Task An_open_action_cannot_be_pointed_outside_the_project_by_a_value()
    {
        var project = Open("""{ "actions": { "report": { "open": "reports/{name}.html" } }, "values": { "name": {} } }""");

        project.SetValue("name", JsonValue.Create(@"..\..\..\Windows\System32\calc"));
        var refused = await project.DoAsync("report");
        project.SetValue("name", JsonValue.Create("weekly"));
        var opened = await project.DoAsync("report");

        Assert.False(refused.Ok);
        Assert.Contains("outside the project folder", refused.Error);
        Assert.True(opened.Ok, opened.Error);
        Assert.Equal(new[] { Path.Combine(_dir.Root, "reports", "weekly.html") }, _shell.Opened);
    }
}
