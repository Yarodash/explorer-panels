using ExplorerPanel.Host;

namespace ExplorerPanel.Tests;

public class HostSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    string File => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Panels_are_on_by_default()
    {
        Assert.True(new HostSettings(File).Enabled);
    }

    [Fact]
    public void Turning_panels_off_survives_a_restart()
    {
        new HostSettings(File) { Enabled = false };

        Assert.False(new HostSettings(File).Enabled);

        new HostSettings(File) { Enabled = true };
        Assert.True(new HostSettings(File).Enabled);
    }

    [Fact]
    public void A_corrupt_settings_file_means_defaults()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{ broken");

        Assert.True(new HostSettings(File).Enabled);
    }
}
