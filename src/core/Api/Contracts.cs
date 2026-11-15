using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExplorerPanel.Core.Api;

// Сообщения местного интерфейса: ими обмениваются фоновая программа, команда explorer-panel и внешние программы.

public sealed record PingResponse(string Version, int Pid);

public sealed record ServiceInfo(string Status, string? Detail);

/// <summary>Состояние проекта, к которому относится папка.</summary>
/// <param name="Root">Корень проекта; null, если у папки нет панели.</param>
/// <param name="Trusted">true — разрешена, false — запрещена, null — владелец ещё не решал.</param>
/// <param name="Loaded">Последняя версия panel.json прочитана без ошибок.</param>
/// <param name="Problems">Замечания к panel.json.</param>
/// <param name="LastError">Последняя ошибка, возникшая при показе панели.</param>
public sealed record StatusResponse(
    string? Root, bool? Trusted, bool Loaded, IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, ServiceInfo> Services, JsonObject Values, string? LastError);

public sealed record FolderRequest(string Folder);
public sealed record SetRequest(string Folder, JsonObject Values);
public sealed record DoRequest(string Folder, string Action);

/// <summary>Итог команды: удалось ли, и если нет — почему.</summary>
public sealed record ApiResult(bool Ok, string? Error = null);

/// <summary>
/// Запись о работающей фоновой программе (файл host.json в профиле пользователя): где она слушает и каким
/// ключом к ней обращаться. Файл лежит в профиле, поэтому прочитать ключ может только сам пользователь.
/// </summary>
public sealed record HostInfo(int Port, int Pid, string Token);

public static class ApiProtocol
{
    public const int DefaultPort = 47631;

    /// <summary>
    /// Заголовок, в котором передаётся ключ из host.json. Без верного ключа запрос отклоняется:
    /// так к программе не достучится ни другой пользователь компьютера, ни страница в браузере.
    /// </summary>
    public const string Header = "X-Explorer-Panel";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
