namespace ExplorerPanel.Core;

/// <summary>Находит проект, к которому относится папка: ближайшего предка с .explorer_components/panel.json.</summary>
public static class ProjectLocator
{
    /// <summary>Полный путь без завершающего разделителя (корень диска остаётся как есть: <c>C:\</c>).</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        int rootLength = Path.GetPathRoot(full)?.Length ?? 0;
        return full.Length > rootLength ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    /// <summary>
    /// Корень проекта для папки или null. Корень определяется по наличию файла, а не по его правильности:
    /// битый panel.json должен показать ошибку, а не заставить панель молча исчезнуть.
    /// </summary>
    public static string? FindRoot(string? folder)
    {
        // Проводник отдаёт и не-папки: "::{…}" для «Этот компьютер», пустую строку для особых страниц
        if (string.IsNullOrWhiteSpace(folder) || folder.StartsWith("::") || !Path.IsPathFullyQualified(folder)) return null;
        try
        {
            var directory = new DirectoryInfo(Normalize(folder));
            if (!directory.Exists) return null;
            for (var current = directory; current != null; current = current.Parent)
                if (File.Exists(PanelLoader.PanelFile(current.FullName)))
                    return Normalize(current.FullName);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
        }
        return null;
    }

    /// <summary>Показывать ли панель проекта в этой папке: "folder" — только в корне, "tree" — в корне и во всех подпапках.</summary>
    public static bool Applies(string root, string folder, string scope)
    {
        string normalizedRoot = Normalize(root), normalizedFolder = Normalize(folder);
        if (string.Equals(normalizedRoot, normalizedFolder, StringComparison.OrdinalIgnoreCase)) return true;
        return scope == "tree"
               && normalizedFolder.StartsWith(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
