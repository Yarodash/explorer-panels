using Microsoft.Win32;

namespace ExplorerPanel.Cli;

/// <summary>
/// Постоянная работа программы: автозапуск при входе в систему и команда <c>explorer-panel</c> в PATH пользователя.
/// Раздел реестра и доступ к PATH передаются снаружи, чтобы это можно было проверить, не трогая настоящие настройки.
/// </summary>
static class InstallCommand
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "ExplorerPanels";
    public const string HostExe = "explorer-panel-host.exe";

    static string Clean(string dir) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));

    static List<string> Split(string path) => path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    static bool Same(string a, string b)
    {
        try { return string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static void Register(string installDir, RegistryKey runKey, Func<string> getPath, Action<string> setPath)
    {
        var dir = Clean(installDir);
        runKey.SetValue(RunValueName, $"\"{Path.Combine(dir, HostExe)}\"");

        var entries = Split(getPath());
        if (entries.Any(entry => Same(entry, dir))) return; // уже есть — второй раз не добавляем
        entries.Add(dir);
        setPath(string.Join(';', entries));
    }

    public static void Unregister(string installDir, RegistryKey runKey, Func<string> getPath, Action<string> setPath)
    {
        runKey.DeleteValue(RunValueName, throwOnMissingValue: false);

        var entries = Split(getPath());
        var kept = entries.Where(entry => !Same(entry, installDir)).ToList();
        if (kept.Count != entries.Count) setPath(string.Join(';', kept));
    }

    /// <summary>Настоящая регистрация для текущего пользователя.</summary>
    public static void RegisterForCurrentUser(string installDir)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        Register(installDir, run, UserPath, SetUserPath);
    }

    public static void UnregisterForCurrentUser(string installDir)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        Unregister(installDir, run, UserPath, SetUserPath);
    }

    static string UserPath() => Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
    static void SetUserPath(string value) => Environment.SetEnvironmentVariable("Path", value, EnvironmentVariableTarget.User);
}
