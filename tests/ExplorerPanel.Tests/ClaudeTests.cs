using System.Diagnostics;
using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

// Кнопки Claude: есть в любой папке, включаются и выключаются в трее.

public class ClaudeSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    string File => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Claude_buttons_are_on_by_default()
    {
        Assert.True(new HostSettings(File).ClaudeButtons);
    }

    [Fact]
    public void Turning_Claude_buttons_off_survives_a_restart()
    {
        new HostSettings(File) { ClaudeButtons = false };

        Assert.False(new HostSettings(File).ClaudeButtons);
    }

    [Fact]
    public void The_two_switches_do_not_reset_each_other()
    {
        new HostSettings(File) { ClaudeButtons = false };
        new HostSettings(File) { Enabled = false };

        var settings = new HostSettings(File);
        Assert.False(settings.ClaudeButtons);
        Assert.False(settings.Enabled);

        new HostSettings(File) { ClaudeButtons = true };
        Assert.False(new HostSettings(File).Enabled);
        Assert.True(new HostSettings(File).ClaudeButtons);
    }
}

public class ClaudeLauncherTests : IDisposable
{
    readonly TempProject _dir = new("sub folder");
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Opens_a_terminal_with_claude_in_the_folder()
    {
        var info = ClaudeLauncher.StartInfo(_dir.Root, resume: false);

        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal("/s /k \"claude\"", info.Arguments);
        Assert.Equal(_dir.Root, info.WorkingDirectory);
        Assert.True(info.UseShellExecute); // так у программы без консоли появляется новое видимое окно терминала
    }

    [Fact]
    public void The_second_button_continues_the_last_conversation()
    {
        Assert.Equal("/s /k \"claude --continue\"", ClaudeLauncher.StartInfo(_dir.Root, resume: true).Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")] // «Этот компьютер»
    [InlineData(@"Z:\no\such\folder\anywhere")]
    [InlineData("relative")]
    public void There_is_nothing_to_open_Claude_in_on_special_pages(string? folder)
    {
        Assert.False(ClaudeLauncher.CanOpenIn(folder));
    }

    [Fact]
    public void Any_real_folder_will_do()
    {
        Assert.True(ClaudeLauncher.CanOpenIn(_dir.Root));
        Assert.True(ClaudeLauncher.CanOpenIn(Path.Combine(_dir.Root, "sub folder")));
        Assert.False(ClaudeLauncher.CanOpenIn(PanelFileOf(_dir))); // файл — не папка
    }

    static string PanelFileOf(TempProject dir)
    {
        var file = Path.Combine(dir.Root, "a file.txt");
        File.WriteAllText(file, "x");
        return file;
    }

    [Fact]
    public async Task The_command_really_starts_in_the_folder_with_its_arguments()
    {
        // Вместо claude — маленький скрипт, который записывает, где и с чем его запустили.
        // Папка с пробелами и кириллицей: она не должна попадать в командную строку.
        var folder = Path.Combine(_dir.Root, "sub folder");
        var script = Path.Combine(_dir.Root, "fake claude.py");
        var marker = Path.Combine(_dir.Root, "launched.txt");
        File.WriteAllText(script, "import io, os, sys\nio.open(sys.argv[1], 'w', encoding='utf-8').write(os.getcwd() + '|' + ' '.join(sys.argv[2:]))\n");

        var info = ClaudeLauncher.StartInfo(folder, resume: true, $"python \"{script}\" \"{marker}\"");
        info.WindowStyle = ProcessWindowStyle.Hidden; // проверка не должна показывать окно на рабочем столе
        using var process = Process.Start(info)!;
        try
        {
            Assert.True(await TestNet.Until(() => File.Exists(marker) && new FileInfo(marker).Length > 0, 15000), "the command did not run");
        }
        finally { process.Kill(entireProcessTree: true); }

        Assert.Equal(folder + "|--continue", File.ReadAllText(marker));
    }
}

public class PanelStatesTests
{
    [Fact]
    public void A_folder_without_a_panel_gets_a_state_with_only_the_Claude_buttons()
    {
        var state = PanelStates.Bare(@"D:\any folder", "ru");

        Assert.True(state["bare"]!.GetValue<bool>());
        Assert.True(state["claude"]!.GetValue<bool>());
        Assert.Equal(@"D:\any folder", state["root"]!.GetValue<string>());
        Assert.Equal("ru", state["locale"]!.GetValue<string>());
        Assert.Empty(state["widgets"]!.AsArray());
    }
}
