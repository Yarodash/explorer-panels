using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExplorerPanel.Host;

/// <summary>
/// Настройки самой программы, которые переживают перезапуск (файл settings.json в папке данных).
/// Оба переключателя меняются из меню значка в трее.
/// </summary>
sealed class HostSettings
{
    readonly string _file;
    readonly JsonObject _values = []; // всё содержимое файла: незнакомые ключи сохраняются как были

    public HostSettings(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject saved) _values = saved;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // испорченный файл настроек — значит, настройки по умолчанию
        }
    }

    /// <summary>Показывать ли панели. Выключение сохраняется: после перезагрузки панели останутся выключенными.</summary>
    public bool Enabled
    {
        get => Get("enabled");
        set => Set("enabled", value);
    }

    /// <summary>Показывать ли во всех папках кнопки Claude («открыть в этой папке» и «продолжить разговор»).</summary>
    public bool ClaudeButtons
    {
        get => Get("claudeButtons");
        set => Set("claudeButtons", value);
    }

    // все переключатели по умолчанию включены
    bool Get(string key) => _values[key] is not JsonValue value || !value.TryGetValue(out bool on) || on;

    void Set(string key, bool value)
    {
        _values[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, _values.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("cannot save settings: " + ex.Message);
        }
    }
}
