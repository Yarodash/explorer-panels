using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ExplorerPanel.Cli;

/// <summary>
/// Постоянная работа программы: автозапуск при входе в систему и команда <c>explorer-panel</c> в PATH пользователя.
/// Разделы реестра передаются снаружи, чтобы это можно было проверить, не трогая настоящие настройки.
/// </summary>
static class InstallCommand
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string EnvironmentKeyPath = "Environment";
    public const string RunValueName = "ExplorerPanels";
    public const string HostExe = "explorer-panel-host.exe";

    static string Clean(string dir) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));

    static List<string> Split(string path) => path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    static bool Same(string entry, string dir)
    {
        try { return string.Equals(Clean(Environment.ExpandEnvironmentVariables(entry)), Clean(dir), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    // PATH читается и пишется как есть, без раскрытия %ПЕРЕМЕННЫХ%: записи вроде %USERPROFILE%\bin
    // должны остаться такими же, а тип значения — «строка с переменными».
    static string RawPath(RegistryKey environment) =>
        environment.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";

    static void WritePath(RegistryKey environment, IEnumerable<string> entries) =>
        environment.SetValue("Path", string.Join(';', entries), RegistryValueKind.ExpandString);

    public static void Register(string installDir, RegistryKey runKey, RegistryKey environmentKey)
    {
        var dir = Clean(installDir);
        runKey.SetValue(RunValueName, $"\"{Path.Combine(dir, HostExe)}\"");

        var entries = Split(RawPath(environmentKey));
        if (entries.Any(entry => Same(entry, dir))) return; // уже есть — второй раз не добавляем
        entries.Add(dir);
        WritePath(environmentKey, entries);
    }

    public static void Unregister(string installDir, RegistryKey runKey, RegistryKey environmentKey)
    {
        runKey.DeleteValue(RunValueName, throwOnMissingValue: false);

        var entries = Split(RawPath(environmentKey));
        var kept = entries.Where(entry => !Same(entry, installDir)).ToList();
        if (kept.Count != entries.Count) WritePath(environmentKey, kept);
    }

    /// <summary>Настоящая регистрация для текущего пользователя.</summary>
    public static void RegisterForCurrentUser(string installDir)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        using var environment = Registry.CurrentUser.CreateSubKey(EnvironmentKeyPath);
        Register(installDir, run, environment);
        AnnounceEnvironmentChange();
    }

    public static void UnregisterForCurrentUser(string installDir)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        using var environment = Registry.CurrentUser.CreateSubKey(EnvironmentKeyPath);
        Unregister(installDir, run, environment);
        AnnounceEnvironmentChange();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    // Сообщает работающим программам, что переменные окружения изменились: новые терминалы увидят обновлённый PATH.
    static void AnnounceEnvironmentChange() =>
        SendMessageTimeout(new IntPtr(0xFFFF) /*HWND_BROADCAST*/, 0x001A /*WM_SETTINGCHANGE*/, IntPtr.Zero, "Environment", 0x0002 /*ABORTIFHUNG*/, 2000, out _);
}
