using System.Text.Json;

namespace ExplorerPanel.Core;

/// <summary>
/// Какие папки проектов владелец разрешил и какие запретил. Пока папка не разрешена, её команды не выполняются.
/// Файл общий для фоновой программы и команды <c>explorer-panel</c>, поэтому перечитывается при каждом обращении,
/// если изменился.
/// </summary>
public sealed class TrustStore(string file)
{
    sealed class Data
    {
        public List<string> Trusted { get; set; } = [];
        public List<string> Blocked { get; set; } = [];
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    readonly object _lock = new();
    Data _data = new();
    (DateTime Time, long Length) _stamp;

    public IReadOnlyList<string> Trusted { get { lock (_lock) { Refresh(); return _data.Trusted.ToList(); } } }
    public IReadOnlyList<string> Blocked { get { lock (_lock) { Refresh(); return _data.Blocked.ToList(); } } }

    /// <summary>true — разрешена, false — запрещена, null — владелец ещё не решал.</summary>
    public bool? Get(string root)
    {
        var key = ProjectLocator.Normalize(root);
        lock (_lock)
        {
            Refresh();
            if (_data.Trusted.Contains(key, Paths)) return true;
            if (_data.Blocked.Contains(key, Paths)) return false;
            return null;
        }
    }

    /// <summary>Запоминает решение; null — забыть папку.</summary>
    public void Set(string root, bool? state)
    {
        var key = ProjectLocator.Normalize(root);
        lock (_lock)
        {
            Refresh();
            _data.Trusted.RemoveAll(p => Paths.Equals(p, key));
            _data.Blocked.RemoveAll(p => Paths.Equals(p, key));
            if (state == true) _data.Trusted.Add(key);
            if (state == false) _data.Blocked.Add(key);
            Save();
        }
    }

    (DateTime, long) Stamp()
    {
        var info = new FileInfo(file);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : default;
    }

    void Refresh()
    {
        var stamp = Stamp();
        if (stamp == _stamp) return;
        _stamp = stamp;
        try
        {
            _data = stamp == default ? new Data() : JsonSerializer.Deserialize<Data>(File.ReadAllText(file), Json) ?? new Data();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _data = new Data(); // испорченный файл равен пустому: лишний раз спросить разрешение безопаснее, чем упасть
        }
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // сначала во временный файл, потом подмена: другой процесс никогда не увидит половину файла
        var temp = file + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, Json));
        File.Move(temp, file, overwrite: true);
        _stamp = Stamp();
    }
}
