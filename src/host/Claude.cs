using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ExplorerPanel.Host;

/// <summary>
/// Кнопки Claude: открывают Claude Code в терминале в той папке, которая показана в Проводнике.
/// В отличие от панели проекта, они есть в любой папке и не зависят от panel.json; включаются и выключаются в трее.
/// </summary>
static class ClaudeLauncher
{
    /// <summary>
    /// Можно ли открыть Claude в том, что показывает окно Проводника: это должна быть настоящая папка на диске,
    /// а не особая страница вроде «Этот компьютер» или «Главная».
    /// </summary>
    public static bool CanOpenIn(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder)) return false;
        try { return Directory.Exists(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>
    /// Как запускается терминал с Claude. Папка передаётся как рабочая, а не в командной строке, поэтому её имя
    /// может быть любым. Запуск через оболочку Windows даёт программе без консоли новое видимое окно; в Windows 11
    /// оно открывается в приложении «Терминал», если оно выбрано терминалом по умолчанию.
    /// </summary>
    /// <param name="resume">Продолжить последний разговор в этой папке (<c>claude --continue</c>).</param>
    /// <param name="command">Что запускать; после выхода из Claude окно остаётся открытым в этой папке.</param>
    public static ProcessStartInfo StartInfo(string folder, bool resume, string command = "claude") =>
        new("cmd.exe", $"/s /k \"{command}{(resume ? " --continue" : "")}\"")
        {
            WorkingDirectory = folder,
            UseShellExecute = true,
        };

    public static void Open(string folder, bool resume)
    {
        // Нажатие пришло нашему окну, поэтому мы вправе разрешить новому окну стать активным; иначе терминал
        // мог бы открыться позади Проводника. «Любому процессу» — потому что окно создаёт не cmd, а приложение «Терминал».
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        using var process = Process.Start(StartInfo(folder, resume));
    }
}

/// <summary>Состояния, которые получает страница панели, когда у папки нет своей панели.</summary>
static class PanelStates
{
    /// <summary>Только кнопки Claude: так выглядит любая папка без panel.json.</summary>
    public static JsonObject Bare(string folder, string locale) => new()
    {
        ["root"] = folder,
        ["locale"] = locale,
        ["trusted"] = true,
        ["collapsed"] = false,
        ["problems"] = new JsonArray(),
        ["widgets"] = new JsonArray(),
        ["services"] = new JsonObject(),
        ["values"] = new JsonObject(),
        ["actions"] = new JsonObject(),
        ["slots"] = new JsonObject(),
        ["claude"] = true,
        ["bare"] = true,
    };
}
