using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

public class ProjectLocatorTests : IDisposable
{
    readonly TempProject _project = new(Path.Combine("src", "deep"));
    public void Dispose() => _project.Dispose();

    [Fact]
    public void FindRoot_walks_up()
    {
        _project.WritePanel("{}");
        Assert.Equal(_project.Root, ProjectLocator.FindRoot(Path.Combine(_project.Root, "src", "deep")));
    }

    [Fact]
    public void FindRoot_from_root_itself()
    {
        _project.WritePanel("{}");
        Assert.Equal(_project.Root, ProjectLocator.FindRoot(_project.Root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FindRoot_returns_null_without_panel()
    {
        Assert.Null(ProjectLocator.FindRoot(Path.Combine(_project.Root, "src", "deep")));
    }

    [Fact]
    public void FindRoot_finds_broken_panel_too()
    {
        // корень определяется по наличию файла: если файл битый, панель покажет ошибку, а не исчезнет молча
        _project.WritePanel("{ broken");
        Assert.Equal(_project.Root, ProjectLocator.FindRoot(_project.Root));
    }

    [Theory]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")] // "Этот компьютер" в Проводнике
    [InlineData("")]
    [InlineData("Z:\\no\\such\\folder")]
    public void FindRoot_ignores_paths_that_are_not_folders(string folder)
    {
        Assert.Null(ProjectLocator.FindRoot(folder));
    }

    [Fact]
    public void Applies_folder_scope_only_root()
    {
        var sub = Path.Combine(_project.Root, "src");

        Assert.True(ProjectLocator.Applies(_project.Root, _project.Root, "folder"));
        Assert.False(ProjectLocator.Applies(_project.Root, sub, "folder"));
        Assert.True(ProjectLocator.Applies(_project.Root, sub, "tree"));
        Assert.True(ProjectLocator.Applies(_project.Root, _project.Root.ToUpperInvariant() + "\\", "folder"));
    }

    [Fact]
    public void Normalize_strips_trailing_separator_but_keeps_drive_root()
    {
        Assert.Equal(@"C:\x", ProjectLocator.Normalize(@"C:\x\"));
        Assert.Equal(@"C:\x", ProjectLocator.Normalize(@"C:\x\y\.."));
        Assert.Equal(@"C:\", ProjectLocator.Normalize(@"C:\"));
    }
}
