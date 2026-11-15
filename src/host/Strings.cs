using System.Globalization;

namespace ExplorerPanel.Host;

/// <summary>Подписи значка в трее — на языке системы (английский или русский).</summary>
static class Strings
{
    public static readonly bool Russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    static string Pick(string en, string ru) => Russian ? ru : en;

    public static string TrayTitle => Pick("Explorer Panels", "Explorer Panels — панели в Проводнике");
    public static string StopServers(string project) => Pick($"Stop servers of {project}", $"Остановить серверы: {project}");
    public static string NoServers => Pick("No servers started from panels", "Нет серверов, запущенных из панелей");
    public static string Allowed => Pick("Allowed folders", "Разрешённые папки");
    public static string Blocked => Pick("Blocked folders", "Запрещённые папки");
    public static string Forget(string folder) => Pick($"Forget {folder}", $"Забыть {folder}");
    public static string None => Pick("(none)", "(нет)");
    public static string Pause => Pick("Pause panels", "Скрыть панели");
    public static string OpenLog => Pick("Open the log", "Открыть журнал");
    public static string Exit => Pick("Exit", "Выход");
}
