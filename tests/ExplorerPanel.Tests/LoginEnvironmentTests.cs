using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

/// <summary>
/// Программа может быть запущена откуда угодно — например, установщиком из терминала внутри Claude Code или Git Bash.
/// Всё, что она запускает сама (серверы, действия, терминал с Claude), должно получать обычное окружение
/// пользователя, а не окружение того терминала.
/// </summary>
public class LoginEnvironmentTests
{
    [Fact]
    public void The_login_environment_has_the_usual_variables_and_nothing_from_this_process()
    {
        var marker = "EP_TEST_MARKER_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(marker, "1");
        try
        {
            var fresh = LoginEnvironment.Read();

            Assert.NotNull(fresh);
            Assert.Contains("Path", fresh!.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("USERPROFILE", fresh.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("SystemRoot", fresh.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(marker, fresh.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(fresh.Keys, name => name.StartsWith('=')); // служебные записи вида "=C:" — не переменные
        }
        finally { Environment.SetEnvironmentVariable(marker, null); }
    }

    [Fact]
    public void Variables_inherited_from_the_launching_terminal_are_dropped()
    {
        var current = new Dictionary<string, string>
        {
            ["CLAUDE_CODE_CHILD_SESSION"] = "1", // с ним Claude, открытый кнопкой, считает себя вложенным сеансом
            ["CLAUDECODE"] = "1",
            ["MSYSTEM"] = "MINGW64",             // от Git Bash
            ["PATH"] = @"C:\Program Files\Git\usr\bin;C:\Windows",
            ["USERPROFILE"] = @"C:\Users\me",
        };
        var fresh = new Dictionary<string, string> { ["Path"] = @"C:\Windows;C:\Users\me\.local\bin", ["USERPROFILE"] = @"C:\Users\me" };

        var (remove, set) = LoginEnvironment.Plan(current, fresh);

        Assert.Equal(new[] { "CLAUDECODE", "CLAUDE_CODE_CHILD_SESSION", "MSYSTEM" }, remove.Order(StringComparer.Ordinal));
        Assert.Equal(@"C:\Windows;C:\Users\me\.local\bin", set["Path"]); // PATH — как при входе в систему, без добавок терминала
    }

    [Fact]
    public void Persistent_user_settings_stay()
    {
        // то, что пользователь задал в настройках системы, есть и в окружении входа — оно остаётся
        var current = new Dictionary<string, string> { ["CLAUDE_CODE_GIT_BASH_PATH"] = @"C:\old\bash.exe" };
        var fresh = new Dictionary<string, string> { ["CLAUDE_CODE_GIT_BASH_PATH"] = @"C:\Program Files\Git\bin\bash.exe" };

        var (remove, set) = LoginEnvironment.Plan(current, fresh);

        Assert.Empty(remove);
        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", set["CLAUDE_CODE_GIT_BASH_PATH"]);
    }

    [Fact]
    public void The_programs_own_variables_stay()
    {
        // ими управляются проверочные экземпляры программы
        var current = new Dictionary<string, string> { ["EXPLORER_PANEL_DATA"] = @"C:\tmp\data", ["explorer_panel_debug"] = "1" };

        var (remove, _) = LoginEnvironment.Plan(current, new Dictionary<string, string>());

        Assert.Empty(remove);
    }
}
