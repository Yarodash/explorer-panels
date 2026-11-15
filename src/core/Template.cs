using System.Text.RegularExpressions;

namespace ExplorerPanel.Core;

/// <summary>
/// Подстановки <c>{имя}</c> в текстах, адресах и командах panel.json.
/// Удвоенная скобка — буквальная: <c>{{</c> даёт <c>{</c>, <c>}}</c> даёт <c>}</c> (например, <c>git rev-list @{{u}}..HEAD</c>).
/// </summary>
public static partial class Template
{
    // удвоенные скобки стоят в выражении первыми: "{{имя}}" — это буквальное "{имя}", а не подстановка в скобках
    [GeneratedRegex(@"\{\{|\}\}|\{([A-Za-z0-9_-]+)\}")]
    private static partial Regex Placeholder();

    // В команду подставляется только такое значение. Пробелы, кавычки, &, |, %, $ и прочее запрещены:
    // иначе название ветки или присланный снаружи текст мог бы стать посторонней командой.
    [GeneratedRegex(@"\A[A-Za-z0-9._\-/:@+]*\z")] // \z, а не $: иначе прошло бы значение с переводом строки в конце
    private static partial Regex SafeForCommand();

    public const string SafeCharactersHint = "letters, digits and . _ - / : @ +";

    // буквальная скобка или null, если это подстановка
    static string? Literal(Match match) => match.Groups[1].Success ? null : match.Value[..1];

    /// <summary>Имена из <c>{…}</c>, каждое один раз, в порядке появления.</summary>
    public static IReadOnlyList<string> Names(string text) =>
        Placeholder().Matches(text).Where(m => m.Groups[1].Success).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Для видимого текста: неизвестное имя заменяется пустой строкой.</summary>
    public static string Fill(string text, Func<string, string?> get) =>
        Placeholder().Replace(text, m => Literal(m) ?? get(m.Groups[1].Value) ?? "");

    /// <summary>Для адресов: значение кодируется как часть адреса.</summary>
    public static string FillUrl(string text, Func<string, string?> get) =>
        Placeholder().Replace(text, m => Literal(m) ?? Uri.EscapeDataString(get(m.Groups[1].Value) ?? ""));

    /// <summary>
    /// Для команд: подставляет значения, только если все они заданы и безопасны: состоят из разрешённых символов
    /// и не начинаются с "-" или "/" там, где команда прочла бы их как свой ключ (в начале аргумента).
    /// Иначе возвращает false и объяснение в <paramref name="error"/>; команду в этом случае выполнять нельзя.
    /// </summary>
    public static bool TryFillCommand(string command, Func<string, string?> get, out string result, out string error)
    {
        string? problem = null;
        var filled = Placeholder().Replace(command, m =>
        {
            if (Literal(m) is { } literal) return literal;
            var name = m.Groups[1].Value;
            var value = get(name);
            if (value == null) problem ??= $"value \"{name}\" is not set";
            else if (!SafeForCommand().IsMatch(value))
                problem ??= $"value \"{name}\" contains characters that are not allowed in a command (allowed: {SafeCharactersHint})";
            else if (value.Length > 0 && value[0] is '-' or '/' && StartsArgument(command, m.Index))
                problem ??= $"value \"{name}\" starts with \"{value[0]}\", which the command would read as an option";
            return value ?? "";
        });
        result = problem == null ? filled : "";
        error = problem ?? "";
        return problem == null;
    }

    // Подстановка стоит в начале аргумента: перед ней начало команды, пробел или кавычка.
    // Значение после "--mode=" или "origin/" ключом стать не может.
    static bool StartsArgument(string command, int index) =>
        index == 0 || char.IsWhiteSpace(command[index - 1]) || command[index - 1] == '"';
}
