using System.Text.RegularExpressions;

namespace ExplorerPanel.Core;

/// <summary>Подстановки <c>{имя}</c> в текстах, адресах и командах panel.json.</summary>
public static partial class Template
{
    [GeneratedRegex(@"\{([A-Za-z0-9_-]+)\}")]
    private static partial Regex Placeholder();

    // В команду подставляется только такое значение. Пробелы, кавычки, &, |, %, $ и прочее запрещены:
    // иначе название ветки или присланный снаружи текст мог бы стать посторонней командой.
    [GeneratedRegex(@"\A[A-Za-z0-9._\-/:@+]*\z")] // \z, а не $: иначе прошло бы значение с переводом строки в конце
    private static partial Regex SafeForCommand();

    public const string SafeCharactersHint = "letters, digits and . _ - / : @ +";

    /// <summary>Имена из <c>{…}</c>, каждое один раз, в порядке появления.</summary>
    public static IReadOnlyList<string> Names(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Для видимого текста: неизвестное имя заменяется пустой строкой.</summary>
    public static string Fill(string text, Func<string, string?> get) =>
        Placeholder().Replace(text, m => get(m.Groups[1].Value) ?? "");

    /// <summary>Для адресов: значение кодируется как часть адреса.</summary>
    public static string FillUrl(string text, Func<string, string?> get) =>
        Placeholder().Replace(text, m => Uri.EscapeDataString(get(m.Groups[1].Value) ?? ""));

    /// <summary>
    /// Для команд: подставляет значения, только если все они заданы и состоят из безопасных символов.
    /// Иначе возвращает false и объяснение в <paramref name="error"/>; команду в этом случае выполнять нельзя.
    /// </summary>
    public static bool TryFillCommand(string command, Func<string, string?> get, out string result, out string error)
    {
        string? problem = null;
        var filled = Placeholder().Replace(command, m =>
        {
            var name = m.Groups[1].Value;
            var value = get(name);
            if (value == null) problem ??= $"value \"{name}\" is not set";
            else if (!SafeForCommand().IsMatch(value))
                problem ??= $"value \"{name}\" contains characters that are not allowed in a command (allowed: {SafeCharactersHint})";
            return value ?? "";
        });
        result = problem == null ? filled : "";
        error = problem ?? "";
        return problem == null;
    }
}
