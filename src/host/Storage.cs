using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExplorerPanel.Host;

/// <summary>Что программа просит у Windows открыть. Вынесено, чтобы в проверках ничего не открывалось на самом деле.</summary>
interface IShell
{
    /// <summary>Открыть адрес, файл или папку тем, чем их открывает Windows.</summary>
    void Open(string target);
    void OpenInNotepad(string file);
}

sealed class WindowsShell : IShell
{
    public void Open(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });

    public void OpenInNotepad(string file) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
}

/// <summary>Лог серверов и действий проекта. Когда файл достигает предела, он становится «.1», и начинается новый.</summary>
sealed class RotatingLog(string file, long maxBytes = 5_000_000)
{
    readonly object _gate = new();

    public string File => file;

    public void Append(string source, string line)
    {
        var text = $"{DateTime.Now:HH:mm:ss} [{source}] {line}{Environment.NewLine}";
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                var info = new FileInfo(file);
                if (info.Exists && info.Length + bytes.Length > maxBytes)
                {
                    try { System.IO.File.Move(file, file + ".1", overwrite: true); }
                    catch (IOException) { } // файл сейчас пишет другой процесс — обрежем в следующий раз
                }
                // В один лог пишут несколько процессов: сама программа и посредники серверов. Поэтому файл
                // открывается с общим доступом, а при столкновении запись повторяется.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                        stream.Write(bytes);
                        break;
                    }
                    catch (IOException) when (attempt < 5) { Thread.Sleep(15); }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // лог не должен ронять программу или сервер
        }
    }
}

/// <summary>Сохраняемое состояние проекта (один JSON-файл в папке данных программы).</summary>
sealed class StateFile
{
    readonly string _file;
    readonly object _gate = new();

    JsonObject Root { get; }

    public StateFile(string file)
    {
        _file = file;
        JsonObject? root = null;
        try
        {
            if (File.Exists(file)) root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // испорченный файл состояния равен пустому
        }
        Root = root ?? [];
    }

    /// <summary>Читает что-нибудь из состояния. Возвращать нужно копию, а не часть самого состояния.</summary>
    public T Read<T>(Func<JsonObject, T> read)
    {
        lock (_gate) return read(Root);
    }

    /// <summary>
    /// Меняет состояние и сохраняет файл. Состояние меняют разные потоки (значения, свёрнутость панели,
    /// запущенные процессы), поэтому изменение и запись идут под одним замком: иначе файл мог записываться
    /// в тот момент, когда другой поток меняет состояние.
    /// </summary>
    public void Update(Action<JsonObject> change)
    {
        lock (_gate)
        {
            change(Root);
            Save();
        }
    }

    /// <summary>Раздел состояния (объект с данным именем); создаётся при первом обращении. Вызывать внутри Read или Update.</summary>
    public static JsonObject Section(JsonObject root, string name)
    {
        if (root[name] is not JsonObject section) root[name] = section = [];
        return section;
    }

    void Save()
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var temp = _file + ".tmp";
                File.WriteAllText(temp, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, _file, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("cannot save state: " + ex.Message);
        }
    }
}

/// <summary>
/// Именованные значения проекта: результаты обработчиков, присланное снаружи, сохранённые переключатели.
/// </summary>
sealed class ValueStore
{
    readonly StateFile _file;
    readonly Dictionary<string, JsonNode?> _values = [];
    readonly object _gate = new();

    public event Action? Changed;

    public ValueStore(StateFile file)
    {
        _file = file;
        file.Read(root =>
        {
            foreach (var (name, value) in StateFile.Section(root, "values")) _values[name] = value?.DeepClone();
            return 0;
        });
    }

    public JsonNode? Get(string name)
    {
        lock (_gate) return _values.GetValueOrDefault(name)?.DeepClone();
    }

    /// <summary>Значение в виде текста для подстановки: строка как есть, число и да/нет — как пишутся в JSON.</summary>
    public string? GetText(string name)
    {
        lock (_gate)
        {
            var node = _values.GetValueOrDefault(name);
            if (node == null) return null;
            return node is JsonValue value && value.TryGetValue(out string? text) ? text : node.ToJsonString();
        }
    }

    /// <param name="persist">Сохранить на диск: значение переживёт перезапуск программы.</param>
    public void Set(string name, JsonNode? value, bool persist)
    {
        lock (_gate)
        {
            if (_values.TryGetValue(name, out var old) && old?.ToJsonString() == value?.ToJsonString()) return;
            _values[name] = value?.DeepClone();
            if (persist) _file.Update(root => StateFile.Section(root, "values")[name] = value?.DeepClone());
        }
        Changed?.Invoke();
    }

    public JsonObject Snapshot()
    {
        var result = new JsonObject();
        lock (_gate)
            foreach (var (name, value) in _values) result[name] = value?.DeepClone();
        return result;
    }
}
