using System.Text.Json;

namespace ExplorerPanel.Core;

/// <summary>
/// Где в тексте JSON находится каждый элемент. Путь записывается как <c>/services/api/health</c> или
/// <c>/widgets/2/icon</c>; для свойства объекта запоминается место его имени, для элемента массива — его начало.
/// Нужно, чтобы сообщить агенту строку и столбец ошибки.
/// </summary>
public static class JsonPositions
{
    public static Dictionary<string, (int Line, int Column)> Map(byte[] utf8, JsonReaderOptions options)
    {
        // начала строк, чтобы перевести смещение в байтах в номер строки и столбца
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < utf8.Length; i++)
            if (utf8[i] == (byte)'\n') lineStarts.Add(i + 1);

        (int, int) Locate(long offset)
        {
            int line = lineStarts.BinarySearch((int)offset);
            if (line < 0) line = ~line - 1;
            return (line + 1, (int)offset - lineStarts[line] + 1);
        }

        var map = new Dictionary<string, (int, int)> { [""] = (1, 1) };
        var path = new List<string>();     // части пути до текущего контейнера
        var arrayIndex = new Stack<int>(); // для открытых массивов — номер следующего элемента; для объектов — -1
        string? pendingProperty = null;    // имя свойства, значение которого сейчас будет прочитано

        var reader = new Utf8JsonReader(utf8, options);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                pendingProperty = reader.GetString() ?? "";
                map["/" + string.Join('/', path.Append(pendingProperty))] = Locate(reader.TokenStartIndex);
                continue;
            }
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                arrayIndex.Pop();
                if (path.Count > 0) path.RemoveAt(path.Count - 1);
                continue;
            }

            // значение: либо свойство объекта, либо очередной элемент массива, либо корень
            string? segment = pendingProperty;
            pendingProperty = null;
            if (segment == null && arrayIndex.Count > 0 && arrayIndex.Peek() >= 0)
            {
                int index = arrayIndex.Pop();
                arrayIndex.Push(index + 1);
                segment = index.ToString();
                map["/" + string.Join('/', path.Append(segment))] = Locate(reader.TokenStartIndex);
            }

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                if (segment != null) path.Add(segment);
                else if (arrayIndex.Count > 0) path.Add(""); // не должно случаться: значение без места в контейнере
                arrayIndex.Push(reader.TokenType == JsonTokenType.StartArray ? 0 : -1);
            }
        }
        return map;
    }
}
