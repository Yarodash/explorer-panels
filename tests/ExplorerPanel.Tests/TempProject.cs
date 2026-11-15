using ExplorerPanel.Core;

namespace ExplorerPanel.Tests;

/// <summary>
/// Временная папка проекта для тестов. В пути намеренно есть пробелы и кириллица:
/// так каждый тест заодно проверяет, что они ничего не ломают.
/// </summary>
sealed class TempProject : IDisposable
{
    public string Root { get; }

    public TempProject(params string[] subfolders)
    {
        Root = Path.Combine(Path.GetTempPath(), "explorer-panel-tests", Guid.NewGuid().ToString("N")[..8], "проект с пробелами");
        Directory.CreateDirectory(Root);
        foreach (var sub in subfolders) Directory.CreateDirectory(Path.Combine(Root, sub));
    }

    public string PanelFile => Path.Combine(Root, PanelLoader.DirName, PanelLoader.FileName);

    public void WritePanel(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PanelFile)!);
        File.WriteAllText(PanelFile, json);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true); } catch { }
    }
}
