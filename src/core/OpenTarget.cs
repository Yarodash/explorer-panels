using System.Text.RegularExpressions;

namespace ExplorerPanel.Core;

/// <summary>Что именно откроет действие <c>open</c> после подстановки значений.</summary>
public static partial class OpenTarget
{
    // "http://", "vscode://" и любая другая схема: это адрес, а не файл
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9+.\-]*://")]
    private static partial Regex Address();

    /// <summary>
    /// Адрес: значения кодируются как часть адреса. Файл или папка: путь считается от корня проекта, и если в нём
    /// есть подстановки, он обязан остаться внутри проекта. Значение может прислать любая программа
    /// (<c>explorer-panel set</c>), поэтому оно вправе выбрать файл в проекте, но не увести наружу — к чужому
    /// файлу, сетевой папке или адресу. То, что автор написал без подстановок, открывается как написано.
    /// </summary>
    public static bool TryResolve(string open, string root, Func<string, string?> get, out string target, out string error)
    {
        error = "";
        if (Address().IsMatch(open))
        {
            target = Template.FillUrl(open, get);
            return true;
        }

        target = "";
        var names = Template.Names(open);
        if (names.FirstOrDefault(name => get(name) == null) is { } missing)
        {
            error = $"value \"{missing}\" is not set";
            return false;
        }

        var filled = Template.Fill(open, get);
        string full;
        try { full = Path.GetFullPath(Path.Combine(root, filled)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"\"{filled}\" is not a valid path";
            return false;
        }

        // двоеточие дальше буквы диска бывает только у адресов и особых имён ("shell:startup"), но не у путей
        bool inside = ProjectLocator.Applies(root, full, "tree") && !filled.Contains(':');
        if (names.Count > 0 && !inside)
        {
            error = $"\"{filled}\" is outside the project folder; a value may only choose a file or folder inside it";
            return false;
        }
        target = full;
        return true;
    }
}
