using System.Globalization;

namespace ExplorerPanel.Host;

/// <summary>Подписи значка в трее — на языке системы (английский или русский).</summary>
static class Strings
{
    public static readonly bool Russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    static string Pick(string en, string ru) => Russian ? ru : en;

    static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    // состояние программы — в подсказке значка и в первой строке меню
    public static string StatusOn(int panels, int servers) => Pick(
        $"Working: {Count(panels, "panel", "panels")} shown, {Count(servers, "server", "servers")} started",
        $"Работает: панелей на экране — {panels}, запущено серверов — {servers}");
    public static string StatusOff => Pick("Turned off: panels are hidden", "Выключено: панели скрыты");

    public static string TurnOff => Pick("Turn panels off", "Выключить панели");
    public static string TurnOn => Pick("Turn panels on", "Включить панели");
    public static string ClaudeButtons => Pick("Claude buttons in every folder", "Кнопки Claude во всех папках");

    public static string StopServers(string project) => Pick($"Stop servers of {project}", $"Остановить серверы: {project}");
    public static string NoServers => Pick("No servers started from panels", "Нет серверов, запущенных из панелей");
    public static string Allowed => Pick("Allowed folders", "Разрешённые папки");
    public static string Blocked => Pick("Blocked folders", "Запрещённые папки");
    public static string Forget(string folder) => Pick($"Forget {folder}", $"Забыть {folder}");
    public static string None => Pick("(none)", "(нет)");
    public static string OpenLog => Pick("Open the log", "Открыть журнал");
    public static string Exit => Pick("Exit", "Выход");
}
