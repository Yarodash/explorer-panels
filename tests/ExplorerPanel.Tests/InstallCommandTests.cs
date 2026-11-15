using ExplorerPanel.Cli;
using Microsoft.Win32;

namespace ExplorerPanel.Tests;

public class InstallCommandTests : IDisposable
{
    // вместо настоящего раздела автозапуска — отдельный раздел реестра, который удаляется после теста
    readonly string _keyName = @"Software\ExplorerPanelsTests\" + Guid.NewGuid().ToString("N")[..8];
    readonly RegistryKey _run;
    string _path = @"C:\a;C:\b";
    const string Dir = @"C:\Users\me\AppData\Local\Programs\ExplorerPanels";

    public InstallCommandTests() => _run = Registry.CurrentUser.CreateSubKey(_keyName);

    public void Dispose()
    {
        _run.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyName, throwOnMissingSubKey: false);
    }

    void Register(string dir = Dir) => InstallCommand.Register(dir, _run, () => _path, value => _path = value);
    void Unregister(string dir = Dir) => InstallCommand.Unregister(dir, _run, () => _path, value => _path = value);

    [Fact]
    public void Register_sets_run_value_and_adds_path_once()
    {
        Register();

        Assert.Equal($"\"{Dir}\\explorer-panel-host.exe\"", _run.GetValue(InstallCommand.RunValueName));
        Assert.Equal($@"C:\a;C:\b;{Dir}", _path);
    }

    [Fact]
    public void Register_twice_does_not_duplicate_path()
    {
        Register();
        Register(Dir.ToUpperInvariant() + "\\"); // тот же путь, записанный иначе

        Assert.Equal($@"C:\a;C:\b;{Dir}", _path);
    }

    [Fact]
    public void Register_into_empty_path()
    {
        _path = "";
        Register();
        Assert.Equal(Dir, _path);
    }

    [Fact]
    public void Unregister_removes_both()
    {
        Register();
        Unregister();

        Assert.Null(_run.GetValue(InstallCommand.RunValueName));
        Assert.Equal(@"C:\a;C:\b", _path);
    }

    [Fact]
    public void Unregister_when_not_registered_changes_nothing()
    {
        Unregister();
        Assert.Equal(@"C:\a;C:\b", _path);
    }
}
