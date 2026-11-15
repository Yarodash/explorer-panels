using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ExplorerPanel.Cli;
using ExplorerPanel.Core;
using ExplorerPanel.Core.Api;
using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

public class CheckCommandTests : IDisposable
{
    readonly TempProject _project = new(Path.Combine("src", "deep"));
    readonly TrustStore _trust;
    readonly StringWriter _output = new();

    public CheckCommandTests() => _trust = new TrustStore(Path.Combine(Path.GetDirectoryName(_project.Root)!, "trust.json"));
    public void Dispose() => _project.Dispose();

    int Check(string? folder = null) => CheckCommand.Run(folder ?? _project.Root, _output, _trust, hostStatus: null);
    string[] Lines => _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Fact]
    public void Check_ok_prints_summary_and_returns_0()
    {
        _project.WritePanel("""{ "services": { "api": { "run": "x" } } }""");

        Assert.Equal(0, Check());
        Assert.Equal(new[]
        {
            "OK: 1 service, 0 actions, 0 values, 4 widgets",
            "host: not running",
            "trusted: not decided (run: explorer-panel trust)",
        }, Lines);
    }

    [Fact]
    public void Check_error_prints_file_line_col_and_returns_1()
    {
        _project.WritePanel("{\n  \"widgets\": [\n    { \"type\": \"button\", \"icon\": \"playy\", \"do\": \"start\" }\n  ]\n}");

        Assert.Equal(1, Check());
        Assert.StartsWith("panel.json:3:25: error: unknown icon \"playy\" (did you mean \"play\"?)", Lines[0]);
        Assert.Equal("FAILED: 1 error, 0 warnings", Lines[1]);
    }

    [Fact]
    public void Check_warning_returns_0()
    {
        _project.WritePanel("""{ "services": { "api": { "run": "x", "helth": "http://localhost:1" } } }""");

        Assert.Equal(0, Check());
        Assert.Contains(Lines, line => line.Contains(": warning: unknown key \"helth\""));
        Assert.Contains("OK: 1 service, 0 actions, 0 values, 4 widgets (1 warning)", Lines);
    }

    [Fact]
    public void Check_without_panel_returns_1()
    {
        Assert.Equal(1, Check());
        Assert.Contains("no .explorer_components/panel.json found", _output.ToString());
    }

    [Fact]
    public void Check_from_subfolder_finds_the_project_root()
    {
        _project.WritePanel("{}");
        Assert.Equal(0, Check(Path.Combine(_project.Root, "src", "deep")));
    }

    [Fact]
    public void Check_reports_trust_decision()
    {
        _project.WritePanel("{}");
        _trust.Set(_project.Root, true);

        Check();

        Assert.Contains("trusted: yes", Lines);
    }

    [Fact]
    public void Check_reports_what_the_running_host_sees()
    {
        _project.WritePanel("{}");
        var status = new StatusResponse(_project.Root, true, Loaded: false, ["panel.json:1:1: error: x"], new Dictionary<string, ServiceInfo>(), [], "TypeError: boom");

        CheckCommand.Run(_project.Root, _output, _trust, status);

        Assert.Contains("host: running", Lines);
        Assert.Contains(Lines, line => line.StartsWith("loaded: no"));
        Assert.Contains("last panel error: TypeError: boom", Lines);
    }
}

public class ValueArgumentTests
{
    [Theory]
    [InlineData("deploy=running", "deploy", "\"running\"")]
    [InlineData("count=3", "count", "3")]
    [InlineData("watch=true", "watch", "true")]
    [InlineData("state={\"state\":\"failed\",\"label\":\"db\"}", "state", "{\"state\":\"failed\",\"label\":\"db\"}")]
    [InlineData("note=a=b", "note", "\"a=b\"")]
    [InlineData("empty=", "empty", "\"\"")]
    public void Parses_name_and_value(string argument, string name, string json)
    {
        Assert.True(Commands.TryParseValue(argument, out var parsedName, out var value));
        Assert.Equal(name, parsedName);
        Assert.Equal(json, value?.ToJsonString());
    }

    [Theory]
    [InlineData("novalue")]
    [InlineData("=x")]
    public void Rejects_arguments_without_a_name(string argument)
    {
        Assert.False(Commands.TryParseValue(argument, out _, out _));
    }
}

/// <summary>Подставная программа-хозяин: запоминает, о чём её просили.</summary>
sealed class FakeHostApi : IHostApi
{
    public JsonObject Values { get; } = [];
    public List<string> Done { get; } = [];
    public List<(string Folder, bool? State)> Trusted { get; } = [];

    public StatusResponse Status(string folder) =>
        new(folder, true, true, [], new Dictionary<string, ServiceInfo> { ["api"] = new("running", null) }, (JsonObject)Values.DeepClone(), null);

    public ApiResult Set(string folder, JsonObject values)
    {
        foreach (var (name, value) in values) Values[name] = value?.DeepClone();
        return new ApiResult(true);
    }

    public Task<ApiResult> Do(string folder, string action)
    {
        Done.Add(action);
        return Task.FromResult(action == "broken" ? new ApiResult(false, "exit code 3") : new ApiResult(true));
    }

    public ApiResult Trust(string folder, bool? state)
    {
        Trusted.Add((folder, state));
        return new ApiResult(true);
    }

    public ApiResult Reload(string folder) => new(true);
}

public class ApiServerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    readonly FakeHostApi _api = new();
    readonly ApiServer _server;
    readonly HostClient _client;
    string InfoFile => Path.Combine(_dir, "host.json");

    public ApiServerTests()
    {
        _server = new ApiServer(_api, InfoFile, TestNet.FreePort());
        _server.Start();
        _client = new HostClient(_server.Port);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Ping_reports_version_and_pid()
    {
        var ping = await _client.PingAsync();
        Assert.Equal(AppInfo.Version, ping.Version);
        Assert.Equal(Environment.ProcessId, ping.Pid);
    }

    [Fact]
    public async Task Rejects_request_without_header()
    {
        // так выглядит запрос со страницы в браузере: без особого заголовка
        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{_server.Port}/v1/ping");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Set_then_status_roundtrip()
    {
        var result = await _client.SetAsync(@"D:\проект", new JsonObject { ["deploy"] = "running", ["count"] = 3 });
        var status = await _client.StatusAsync(@"D:\проект");

        Assert.True(result.Ok);
        Assert.Equal(@"D:\проект", status.Root);
        Assert.Equal("running", (string?)status.Values["deploy"]);
        Assert.Equal(3, (int?)status.Values["count"]);
        Assert.Equal("running", status.Services["api"].Status);
    }

    [Fact]
    public async Task Do_returns_the_action_result()
    {
        Assert.True((await _client.DoAsync(@"D:\p", "tests")).Ok);

        var failed = await _client.DoAsync(@"D:\p", "broken");
        Assert.False(failed.Ok);
        Assert.Equal("exit code 3", failed.Error);
        Assert.Equal(new[] { "tests", "broken" }, _api.Done);
    }

    [Fact]
    public async Task Trust_passes_the_decision()
    {
        await _client.TrustAsync(@"D:\p", true);
        await _client.TrustAsync(@"D:\p", null);
        Assert.Equal(new (string, bool?)[] { (@"D:\p", true), (@"D:\p", null) }, _api.Trusted);
    }

    [Fact]
    public async Task Unknown_path_is_404_and_bad_body_is_400()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add(ApiProtocol.Header, "1");

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{_server.Port}/v1/nope")).StatusCode);
        var bad = await http.PostAsync($"http://127.0.0.1:{_server.Port}/v1/set", new StringContent("{ not json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Host_info_file_lets_the_client_find_the_server()
    {
        using var found = await HostClient.TryConnectAsync(InfoFile);
        Assert.NotNull(found);
        Assert.Equal(_server.Port, found.Port);
    }

    [Fact]
    public async Task Client_does_not_connect_to_a_stale_host_info_file()
    {
        Directory.CreateDirectory(_dir);
        var stale = Path.Combine(_dir, "stale.json");
        File.WriteAllText(stale, $$"""{ "port": {{TestNet.FreePort()}}, "pid": 1 }""");

        Assert.Null(await HostClient.TryConnectAsync(stale));
        Assert.Null(await HostClient.TryConnectAsync(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void Port_falls_forward_when_busy()
    {
        var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        try
        {
            int taken = ((IPEndPoint)busy.LocalEndpoint).Port;
            using var second = new ApiServer(_api, Path.Combine(_dir, "second.json"), taken);
            second.Start();
            Assert.InRange(second.Port, taken + 1, taken + 10);
        }
        finally { busy.Stop(); }
    }
}
