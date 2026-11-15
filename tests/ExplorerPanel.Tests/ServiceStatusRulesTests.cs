using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

public class ServiceStatusRulesTests
{
    // сервер с проверочным адресом, который никто не запускал и который не отвечает
    static readonly ServiceObservation Idle = new(
        HasHealth: true, Healthy: false, OwnProcessAlive: false, ExitCode: null, SecondsSinceExit: 0,
        WantRunning: false, PortOwner: PortOwner.None, ForeignName: null, ForeignPid: null);

    static ServiceStatus Status(ServiceObservation o) => ServiceStatusRules.Compute(o).Status;

    [Fact]
    public void Never_started_is_stopped() => Assert.Equal(ServiceStatus.Stopped, Status(Idle));

    [Fact]
    public void Foreign_port_owner_is_failed_with_detail()
    {
        var (status, detail) = ServiceStatusRules.Compute(Idle with
        {
            Healthy = true, PortOwner = PortOwner.Foreign, ForeignName = "node.exe", ForeignPid = 4242,
        });

        Assert.Equal(ServiceStatus.Failed, status);
        Assert.Equal("port in use by another program: node.exe (4242)", detail);
    }

    [Fact]
    public void Healthy_is_running()
    {
        Assert.Equal(ServiceStatus.Running, Status(Idle with { Healthy = true, OwnProcessAlive = true, WantRunning = true }));
    }

    [Fact]
    public void Healthy_server_started_outside_the_panel_is_running()
    {
        Assert.Equal(ServiceStatus.Running, Status(Idle with { Healthy = true, PortOwner = PortOwner.Project }));
    }

    [Fact]
    public void Alive_without_response_is_starting()
    {
        Assert.Equal(ServiceStatus.Starting, Status(Idle with { OwnProcessAlive = true, WantRunning = true }));
    }

    [Fact]
    public void Alive_without_health_is_running()
    {
        Assert.Equal(ServiceStatus.Running, Status(Idle with { HasHealth = false, OwnProcessAlive = true, WantRunning = true }));
    }

    [Fact]
    public void Launcher_exit_zero_is_starting_within_grace()
    {
        Assert.Equal(ServiceStatus.Starting, Status(Idle with { ExitCode = 0, SecondsSinceExit = 5, WantRunning = true }));
    }

    [Fact]
    public void Launcher_exit_zero_fails_after_grace()
    {
        var (status, detail) = ServiceStatusRules.Compute(Idle with
        {
            ExitCode = 0, SecondsSinceExit = ServiceStatusRules.LauncherGraceSeconds + 1, WantRunning = true,
        });

        Assert.Equal(ServiceStatus.Failed, status);
        Assert.Contains("did not start responding", detail);
    }

    [Fact]
    public void Nonzero_exit_is_failed()
    {
        var (status, detail) = ServiceStatusRules.Compute(Idle with { ExitCode = 3, SecondsSinceExit = 1, WantRunning = true });

        Assert.Equal(ServiceStatus.Failed, status);
        Assert.Equal("exited with code 3", detail);
    }

    [Fact]
    public void Exit_zero_without_health_while_wanted_is_failed()
    {
        // без проверочного адреса «работает» значит «процесс жив»; раз процесс завершился сам — сервер не работает
        Assert.Equal(ServiceStatus.Failed, Status(Idle with { HasHealth = false, ExitCode = 0, SecondsSinceExit = 1, WantRunning = true }));
    }

    [Fact]
    public void Stopped_by_user_is_stopped()
    {
        Assert.Equal(ServiceStatus.Stopped, Status(Idle with { ExitCode = -1, SecondsSinceExit = 1, WantRunning = false }));
    }
}

public class HandlerOutputTests
{
    [Fact]
    public void Plain_text_is_value()
    {
        var result = HandlerOutput.Parse("main\r\n");

        Assert.Equal("main", (string?)result.Value);
        Assert.Null(result.Ttl);
        Assert.Null(result.Widgets);
    }

    [Fact]
    public void Json_scalar_is_value()
    {
        Assert.Equal(42, (int?)HandlerOutput.Parse("42").Value);
        Assert.True((bool?)HandlerOutput.Parse(" true ").Value);
    }

    [Fact]
    public void Envelope_with_value_ttl_widgets()
    {
        var result = HandlerOutput.Parse("""
            { "value": "3 new", "ttl": 120, "widgets": [ { "type": "text", "text": "3 new commits" } ] }
            """);

        Assert.Equal("3 new", (string?)result.Value);
        Assert.Equal(120, result.Ttl);
        Assert.Equal("text", (string?)Assert.Single(result.Widgets!)!["type"]);
    }

    [Fact]
    public void Object_without_envelope_keys_is_value()
    {
        var result = HandlerOutput.Parse("""{ "state": "running", "label": "deploy" }""");

        Assert.Equal("running", (string?)result.Value!["state"]);
        Assert.Null(result.Ttl);
    }

    [Fact]
    public void Empty_output_is_null()
    {
        Assert.Null(HandlerOutput.Parse("  \r\n").Value);
    }

    [Fact]
    public void Multiline_text_stays_text()
    {
        Assert.Equal("line 1\nline 2", (string?)HandlerOutput.Parse("line 1\nline 2\n").Value);
    }

    [Fact]
    public void Nonpositive_ttl_is_ignored()
    {
        Assert.Null(HandlerOutput.Parse("""{ "value": 1, "ttl": 0 }""").Ttl);
    }
}
