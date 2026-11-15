namespace ExplorerPanel.Core;

/// <summary>Название и версия программы.</summary>
public static class AppInfo
{
    public const string Name = "Explorer Panels";

    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
