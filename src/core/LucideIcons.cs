namespace ExplorerPanel.Core;

/// <summary>
/// Имена иконок lucide (kebab-case), которые умеет показывать страница панели.
/// Список выгружается из пакета lucide-react скриптом src/ui/scripts/export-icons.mjs.
/// </summary>
public static class LucideIcons
{
    static readonly Lazy<HashSet<string>> Names = new(() =>
    {
        using var stream = typeof(LucideIcons).Assembly.GetManifestResourceStream("lucide-icons.txt")
                           ?? throw new InvalidOperationException("lucide-icons.txt is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
    });

    public static bool Exists(string name) => Names.Value.Contains(name);

    public static IEnumerable<string> All => Names.Value;
}
