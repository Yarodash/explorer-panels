using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

public class TrustStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8]);
    string File => Path.Combine(_dir, "trust.json");
    const string Root = @"D:\проект с пробелами";

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Unknown_folder_is_not_decided()
    {
        Assert.Null(new TrustStore(File).Get(Root));
    }

    [Fact]
    public void Trust_roundtrip_survives_reload()
    {
        new TrustStore(File).Set(Root, true);

        var reloaded = new TrustStore(File);
        Assert.True(reloaded.Get(Root));
        Assert.Equal(new[] { Root }, reloaded.Trusted);
        Assert.Empty(reloaded.Blocked);
    }

    [Fact]
    public void Blocked_is_remembered()
    {
        var store = new TrustStore(File);
        store.Set(Root, false);

        Assert.False(new TrustStore(File).Get(Root));
        Assert.Equal(new[] { Root }, store.Blocked);
        Assert.Empty(store.Trusted);
    }

    [Fact]
    public void Trust_is_case_insensitive_and_ignores_trailing_slash()
    {
        var store = new TrustStore(File);
        store.Set(Root, true);

        Assert.True(store.Get(Root.ToUpperInvariant() + "\\"));
    }

    [Fact]
    public void Trust_set_null_forgets()
    {
        var store = new TrustStore(File);
        store.Set(Root, true);
        store.Set(Root, null);

        Assert.Null(new TrustStore(File).Get(Root));
    }

    [Fact]
    public void Changing_decision_moves_folder_between_lists()
    {
        var store = new TrustStore(File);
        store.Set(Root, false);
        store.Set(Root, true);

        Assert.Equal(new[] { Root }, store.Trusted);
        Assert.Empty(store.Blocked);
    }

    [Fact]
    public void Corrupt_file_is_treated_as_empty()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{ not json");

        var store = new TrustStore(File);
        Assert.Null(store.Get(Root));
        store.Set(Root, true); // и после этого файл снова пригоден
        Assert.True(new TrustStore(File).Get(Root));
    }

    [Fact]
    public void Sees_changes_made_by_another_process()
    {
        // команда "explorer-panel trust" и фоновая программа — разные процессы с одним файлом
        var host = new TrustStore(File);
        var cli = new TrustStore(File);
        cli.Set(Root, true);

        Assert.True(host.Get(Root));
    }
}

public class AppPathsTests
{
    [Fact]
    public void State_file_is_stable_for_the_same_folder_and_differs_between_folders()
    {
        var a = AppPaths.StateFileIn(@"C:\data", @"D:\Проект");
        var same = AppPaths.StateFileIn(@"C:\data", @"d:\проект\");
        var other = AppPaths.StateFileIn(@"C:\data", @"D:\Проект2");

        Assert.Equal(a, same);
        Assert.NotEqual(a, other);
        Assert.StartsWith(@"C:\data\state\", a);
        Assert.EndsWith(".json", a);
    }
}
