using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        Assert.NotNull(typeof(AppInfo).Assembly);
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }
}
