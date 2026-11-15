namespace ExplorerPanel.Core;

/// <summary>Стандартные виджеты панели. Список совпадает с перечислением в schema/panel.schema.json.</summary>
public static class WidgetTypes
{
    public const string Status = "status", Button = "button", Text = "text", Toggle = "toggle", Select = "select",
        Progress = "progress", Menu = "menu", Clock = "clock", Stopwatch = "stopwatch", Calendar = "calendar",
        Separator = "separator", Slot = "slot";

    public static readonly IReadOnlyList<string> All =
    [
        Status, Button, Text, Toggle, Select, Progress, Menu, Clock, Stopwatch, Calendar, Separator, Slot,
    ];
}

/// <summary>Действия, которые есть в любой панели и не описываются в "actions".</summary>
public static class BuiltinActions
{
    public const string Start = "start", Stop = "stop", Restart = "restart", Logs = "logs";

    /// <summary>Действия над серверами: без уточнения — над всеми, с "имя:сервер" — над одним.</summary>
    public static readonly IReadOnlyList<string> ServiceVerbs = [Start, Stop, Restart];

    public static readonly IReadOnlyList<string> All = [Start, Stop, Restart, Logs];

    /// <summary>Разбирает "start" или "restart:api". Возвращает false, если это не встроенное действие.</summary>
    public static bool TryParse(string name, out string verb, out string? service)
    {
        int colon = name.IndexOf(':');
        verb = colon < 0 ? name : name[..colon];
        service = colon < 0 ? null : name[(colon + 1)..];
        if (service != null) return ServiceVerbs.Contains(verb);
        return All.Contains(verb);
    }

    public static bool IsBuiltin(string name) => TryParse(name, out _, out _);
}
