using System.Text.Json.Nodes;

namespace ExplorerPanel.Core;

/// <summary>Замечание к panel.json: ошибка или предупреждение с местом в файле (строка и столбец с единицы).</summary>
public sealed record Problem(string File, int Line, int Column, bool IsError, string Message)
{
    public override string ToString() => $"{File}:{Line}:{Column}: {(IsError ? "error" : "warning")}: {Message}";
}

/// <summary>Как узнать, что сервер жив: по адресу или по команде (код возврата 0).</summary>
public sealed record HealthDef(string? Url, string? Run);

/// <summary>Долгоживущий процесс проекта.</summary>
public sealed record ServiceDef(
    string Name, string Run, string? Cwd, IReadOnlyDictionary<string, string> Env, HealthDef? Health, string? Stop);

/// <summary>Разовое действие: команда либо открытие адреса, файла или папки.</summary>
public sealed record ActionDef(
    string Name, string? Run, string? Open, string? Cwd, IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> Needs);

/// <summary>
/// Именованное значение. С <see cref="Run"/> — результат команды, которая запускается раз в <see cref="Every"/> секунд.
/// Без него — значение, которое хранит программа или присылают снаружи.
/// </summary>
public sealed record ValueDef(string Name, string? Run, double Every, string? Cwd, JsonNode? Default);

/// <summary>Проверенное содержимое panel.json.</summary>
public sealed record PanelConfig(
    string Scope,
    IReadOnlyList<ServiceDef> Services,
    IReadOnlyList<ActionDef> Actions,
    IReadOnlyList<ValueDef> Values,
    JsonArray Widgets) // виджеты для страницы; если в файле их нет — построены по умолчанию
{
    public const double DefaultEvery = 30;
}

/// <summary>Итог чтения panel.json. <see cref="Config"/> есть, только если нет ошибок (предупреждения допустимы).</summary>
public sealed record LoadResult(PanelConfig? Config, IReadOnlyList<Problem> Problems)
{
    public bool Ok => Config != null && !Problems.Any(p => p.IsError);
}
