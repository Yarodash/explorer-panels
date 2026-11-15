using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

static class TestNet
{
    /// <summary>Свободный порт на этом компьютере.</summary>
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static bool IsListening(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, port).Wait(500) && client.Connected;
        }
        catch { return false; }
    }

    public static async Task<bool> Until(Func<bool> condition, int timeoutMs = 20000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }
}

public class ProcessInfoTests : IDisposable
{
    readonly TempProject _project = new();
    public void Dispose() => _project.Dispose();

    [Fact]
    public void ListeningPids_finds_own_listener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.Contains(Environment.ProcessId, ProcessInfo.ListeningPids(port));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void ListeningPids_is_empty_for_a_free_port()
    {
        Assert.Empty(ProcessInfo.ListeningPids(TestNet.FreePort()));
    }

    [Fact]
    public void WorkingDirectory_of_child()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
        {
            WorkingDirectory = _project.Root, CreateNoWindow = true, UseShellExecute = false,
        })!;
        try
        {
            Assert.Equal(_project.Root, ProcessInfo.WorkingDirectory(child.Id)?.TrimEnd('\\'), ignoreCase: true);
        }
        finally { child.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void WorkingDirectory_of_missing_process_is_null()
    {
        Assert.Null(ProcessInfo.WorkingDirectory(-1));
    }

    [Fact]
    public async Task KillTree_kills_grandchild()
    {
        // cmd -> cmd -> ping: убить нужно всех троих
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c cmd /c ping -n 60 127.0.0.1 >nul")
        {
            CreateNoWindow = true, UseShellExecute = false,
        })!;
        Assert.True(await TestNet.Until(() => ProcessInfo.Descendants(parent.Id).Count >= 2, 5000));
        var descendants = ProcessInfo.Descendants(parent.Id);

        ProcessInfo.KillTree(parent.Id);

        Assert.True(await TestNet.Until(() => parent.HasExited && descendants.All(pid => !ProcessInfo.IsAlive(pid)), 5000));
    }

    [Fact]
    public void ParentChain_leads_to_the_test_process()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.Contains(Environment.ProcessId, ProcessInfo.ParentChain(child.Id, 3));
        }
        finally { child.Kill(entireProcessTree: true); }
    }
}

public class CommandRunnerTests : IDisposable
{
    readonly TempProject _project = new();
    public void Dispose() => _project.Dispose();

    static readonly Dictionary<string, string> NoEnv = [];

    [Fact]
    public async Task Runs_in_cwd_and_captures_output()
    {
        var result = await CommandRunner.RunAsync("cd", _project.Root, NoEnv, TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(_project.Root, result.Output.Trim(), ignoreCase: true); // кириллица и пробелы в пути доходят без искажений
    }

    [Fact]
    public async Task Reports_exit_code()
    {
        var result = await CommandRunner.RunAsync("exit 7", _project.Root, NoEnv, TimeSpan.FromSeconds(20));
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task Passes_env()
    {
        var env = new Dictionary<string, string> { ["PANEL_TEST"] = "привет" };
        var result = await CommandRunner.RunAsync("echo %PANEL_TEST%", _project.Root, env, TimeSpan.FromSeconds(20));
        Assert.Equal("привет", result.Output.Trim());
    }

    [Fact]
    public async Task Handles_quotes_and_pipes_like_a_terminal()
    {
        var result = await CommandRunner.RunAsync("echo \"a b\" | findstr \"a b\"", _project.Root, NoEnv, TimeSpan.FromSeconds(20));
        Assert.Equal("\"a b\"", result.Output.Trim());
    }

    [Fact]
    public async Task Timeout_kills_process()
    {
        var watch = Stopwatch.StartNew();
        var result = await CommandRunner.RunAsync("ping -n 60 127.0.0.1 >nul", _project.Root, NoEnv, TimeSpan.FromSeconds(1));

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Contains("timed out", result.Output);
    }

    [Fact]
    public async Task Start_streams_lines_and_exits()
    {
        var lines = new List<string>();
        using var process = CommandRunner.Start("echo one& echo two", _project.Root, NoEnv, line => { lock (lines) lines.Add(line); });
        await process.WaitForExitAsync();

        Assert.True(await TestNet.Until(() => { lock (lines) return lines.Count == 2; }, 3000));
        Assert.Equal(new[] { "one", "two" }, lines);
    }
}

public class RotatingLogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Writes_lines_with_source()
    {
        var file = Path.Combine(_dir, "logs", "services.log");
        new RotatingLog(file).Append("api", "started");

        Assert.EndsWith(" [api] started", File.ReadAllLines(file).Single());
    }

    [Fact]
    public void Rotates_when_over_limit()
    {
        var file = Path.Combine(_dir, "services.log");
        var log = new RotatingLog(file, maxBytes: 2000);

        for (int i = 0; i < 200; i++) log.Append("api", $"line {i} {new string('x', 40)}");

        Assert.True(new FileInfo(file).Length <= 2000);
        Assert.True(File.Exists(file + ".1"));
        Assert.Contains("line 199", File.ReadAllText(file)); // самое свежее — в основном файле
    }
}

public class ValueStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    string File => Path.Combine(_dir, "state", "abc.json");
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Persisted_value_survives_reload()
    {
        var store = new ValueStore(new StateFile(File));
        store.Set("env", JsonValue.Create("prod"), persist: true);
        store.Set("branch", JsonValue.Create("main"), persist: false);

        var reloaded = new ValueStore(new StateFile(File));
        Assert.Equal("prod", reloaded.GetText("env"));
        Assert.Null(reloaded.Get("branch")); // результат обработчика не хранится: он вычисляется заново
    }

    [Fact]
    public void GetText_formats_scalars()
    {
        var store = new ValueStore(new StateFile(File));
        store.Set("flag", JsonValue.Create(true), persist: false);
        store.Set("count", JsonValue.Create(3), persist: false);
        store.Set("object", new JsonObject { ["state"] = "running" }, persist: false);

        Assert.Equal("true", store.GetText("flag"));
        Assert.Equal("3", store.GetText("count"));
        Assert.Equal("""{"state":"running"}""", store.GetText("object"));
        Assert.Null(store.GetText("missing"));
    }

    [Fact]
    public void Changed_fires_only_when_value_differs()
    {
        var store = new ValueStore(new StateFile(File));
        int changes = 0;
        store.Changed += () => changes++;

        store.Set("env", JsonValue.Create("dev"), persist: false);
        store.Set("env", JsonValue.Create("dev"), persist: false);
        store.Set("env", JsonValue.Create("prod"), persist: false);

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Corrupt_state_file_is_ignored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, "{ broken");

        var store = new ValueStore(new StateFile(File));
        store.Set("env", JsonValue.Create("dev"), persist: true);

        Assert.Equal("dev", new ValueStore(new StateFile(File)).GetText("env"));
    }
}
