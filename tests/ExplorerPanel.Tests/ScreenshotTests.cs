using ExplorerPanel.Cli;
using ExplorerPanel.Core;
using ExplorerPanel.Core.Api;
using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

// Команда explorer-panel screenshot: снимок панели в файл, чтобы агент видел, что получилось.

public class ScreenshotApiTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    readonly FakeHostApi _api = new();
    readonly ApiServer _server;
    readonly HostClient _client;

    public ScreenshotApiTests()
    {
        _server = new ApiServer(_api, Path.Combine(_dir, "host.json"), TestNet.FreePort());
        _server.Start();
        _client = new HostClient(_server.Port, _server.Token);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task The_host_is_asked_for_a_picture_of_the_folders_panel()
    {
        var result = await _client.ScreenshotAsync(@"D:\p", @"D:\p\shot.png");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(new[] { (@"D:\p", @"D:\p\shot.png") }, _api.Shots);
    }
}

public class ScreenshotArgsTests : IDisposable
{
    readonly TempProject _project = new("sub");
    public void Dispose() => _project.Dispose();

    [Fact]
    public void An_option_is_taken_out_of_the_arguments()
    {
        string[] args = ["--out", "shot.png", "sub"];

        Assert.Equal("shot.png", Commands.TakeOption(ref args, "--out"));
        Assert.Equal(new[] { "sub" }, args);
        Assert.Null(Commands.TakeOption(ref args, "--out"));
    }

    [Fact]
    public void By_default_the_picture_goes_next_to_the_logs_which_git_ignores()
    {
        _project.WritePanel("{}");

        var file = Commands.ScreenshotFile(Path.Combine(_project.Root, "sub"), given: null, cwd: _project.Root);

        Assert.Equal(Path.Combine(_project.Root, PanelLoader.DirName, "logs", "panel.png"), file);
    }

    [Fact]
    public void A_given_file_is_relative_to_the_current_folder()
    {
        _project.WritePanel("{}");

        Assert.Equal(Path.Combine(_project.Root, "shots", "a.png"), Commands.ScreenshotFile(_project.Root, @"shots\a.png", cwd: _project.Root));
    }

    [Fact]
    public void A_folder_without_a_panel_gets_a_picture_in_the_temporary_folder()
    {
        // в такой папке есть только кнопки Claude; класть туда файл без спроса не нужно
        var file = Commands.ScreenshotFile(_project.Root, given: null, cwd: _project.Root);

        Assert.StartsWith(Path.GetTempPath(), file);
        Assert.EndsWith(".png", file);
    }
}
