using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExplorerPanel.Core;

/// <summary>Что вернул обработчик значения.</summary>
/// <param name="Value">Новое значение.</param>
/// <param name="Ttl">Через сколько секунд запустить обработчик снова (если он сам это указал).</param>
/// <param name="Widgets">Виджеты, которые обработчик просит показать в месте <c>slot</c>.</param>
public sealed record HandlerResult(JsonNode? Value, double? Ttl, JsonArray? Widgets);

/// <summary>
/// Разбор вывода команды-обработчика.
/// Обычный текст — это значение. JSON — тоже значение, кроме объекта-«конверта» с ключами
/// <c>value</c>, <c>ttl</c>, <c>widgets</c>: так скрипт сам отдаёт значение, срок его жизни и компоненты.
/// </summary>
public static class HandlerOutput
{
    static readonly string[] EnvelopeKeys = ["value", "ttl", "widgets"];

    public static HandlerResult Parse(string stdout)
    {
        var text = stdout.Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return new HandlerResult(null, null, null);

        JsonNode? node;
        try { node = JsonNode.Parse(text); }
        catch (JsonException) { return new HandlerResult(JsonValue.Create(text), null, null); }

        if (node is not JsonObject envelope || !EnvelopeKeys.Any(envelope.ContainsKey))
            return new HandlerResult(node, null, null);

        double? ttl = null;
        if (envelope["ttl"] is JsonValue ttlValue && ttlValue.TryGetValue(out double seconds) && seconds > 0) ttl = seconds;
        return new HandlerResult(envelope["value"]?.DeepClone(), ttl, envelope["widgets"]?.DeepClone() as JsonArray);
    }
}
