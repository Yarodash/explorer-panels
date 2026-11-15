using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExplorerPanel.Host;

/// <summary>Настройки самой программы, которые переживают перезапуск (файл settings.json в папке данных).</summary>
sealed class HostSettings
{
    readonly string _file;
    bool _enabled = true;

    public HostSettings(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file))?["enabled"] is JsonValue value && value.TryGetValue(out bool enabled))
                _enabled = enabled;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // испорченный файл настроек — значит, настройки по умолчанию
        }
    }

    /// <summary>Показывать ли панели. Выключение из трея сохраняется: после перезагрузки панели останутся выключенными.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file, new JsonObject { ["enabled"] = value }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Write("cannot save settings: " + ex.Message);
            }
        }
    }
}
