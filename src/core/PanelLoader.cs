using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ExplorerPanel.Core;

/// <summary>
/// Читает и проверяет <c>.explorer_components/panel.json</c>.
/// Сообщения — на английском и с местом в файле: их читает агент, который пишет панель.
/// </summary>
public static partial class PanelLoader
{
    public const string DirName = ".explorer_components";
    public const string FileName = "panel.json";

    // Комментарии и висячие запятые допускаются: агенты и люди часто их оставляют, а смысл файла от этого не меняется.
    static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex NamePattern();

    public static string PanelFile(string projectRoot) => Path.Combine(projectRoot, DirName, FileName);

    public static LoadResult LoadFile(string projectRoot)
    {
        string json;
        try { json = File.ReadAllText(PanelFile(projectRoot)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new LoadResult(null, [new Problem(FileName, 1, 1, true, $"{DirName}/{FileName} not found in {projectRoot}")]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LoadResult(null, [new Problem(FileName, 1, 1, true, "cannot read the file: " + ex.Message)]);
        }
        return Parse(json, projectRoot);
    }

    public static LoadResult Parse(string json, string projectRoot)
    {
        var utf8 = Encoding.UTF8.GetBytes(json);
        JsonDocument document;
        try { document = JsonDocument.Parse(utf8, DocumentOptions); }
        catch (JsonException ex)
        {
            int line = (int)(ex.LineNumber ?? 0) + 1, column = (int)(ex.BytePositionInLine ?? 0) + 1;
            return new LoadResult(null, [new Problem(FileName, line, column, true, "invalid JSON: " + CleanMessage(ex.Message))]);
        }

        using (document)
        {
            var validator = new Validator(projectRoot, JsonPositions.Map(utf8, ReaderOptions));
            var config = validator.Run(document.RootElement);
            var problems = validator.Problems.OrderBy(p => p.Line).ThenBy(p => p.Column).ToList();
            return new LoadResult(problems.Any(p => p.IsError) ? null : config, problems);
        }
    }

    // у сообщений System.Text.Json в конце идут служебные "Path: … | LineNumber: … | BytePositionInLine: …"
    static string CleanMessage(string message)
    {
        foreach (var marker in new[] { " Path:", " LineNumber:" })
        {
            int at = message.IndexOf(marker, StringComparison.Ordinal);
            if (at > 0) message = message[..at];
        }
        return message.Trim();
    }

    static bool IsHttpUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    sealed class Validator(string projectRoot, Dictionary<string, (int Line, int Column)> positions)
    {
        public List<Problem> Problems { get; } = [];

        // имена, на которые можно ссылаться; собираются заранее, чтобы порядок разделов в файле не имел значения
        readonly HashSet<string> _services = [], _actions = [], _values = [];
        int _errors;

        // ───────── сообщения ─────────

        void Error(string path, string message) { _errors++; Add(path, true, message); }
        void Warn(string path, string message) => Add(path, false, message);

        void Add(string path, bool isError, string message)
        {
            // у несуществующего элемента (например, пропущенного обязательного ключа) берём место ближайшего предка
            (int Line, int Column) at;
            while (!positions.TryGetValue(path, out at))
            {
                int cut = path.LastIndexOf('/');
                if (cut < 0) { at = (1, 1); break; }
                path = path[..cut];
            }
            Problems.Add(new Problem(FileName, at.Line, at.Column, isError, message));
        }

        string UnknownService(string name) => $"unknown service \"{name}\"{Suggest.Hint(name, _services)}";

        // ───────── чтение полей ─────────

        void CheckKeys(JsonElement obj, string path, params string[] allowed)
        {
            foreach (var member in obj.EnumerateObject())
                if (!allowed.Contains(member.Name))
                    Warn($"{path}/{member.Name}", $"unknown key \"{member.Name}\"{Suggest.Hint(member.Name, allowed)}");
        }

        string? Str(JsonElement obj, string path, string key, bool required = false)
        {
            if (!obj.TryGetProperty(key, out var value))
            {
                if (required) Error(path, $"\"{key}\" is required");
                return null;
            }
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text) return text;
            Error($"{path}/{key}", $"\"{key}\" must be a non-empty string");
            return null;
        }

        void Bool(JsonElement obj, string path, string key)
        {
            if (obj.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                Error($"{path}/{key}", $"\"{key}\" must be true or false");
        }

        string? Cwd(JsonElement obj, string path)
        {
            var cwd = Str(obj, path, "cwd");
            if (cwd != null && !Directory.Exists(Path.GetFullPath(Path.Combine(projectRoot, cwd))))
                Error($"{path}/cwd", $"cwd \"{cwd}\" does not exist (it is relative to the project root)");
            return cwd;
        }

        Dictionary<string, string> Env(JsonElement obj, string path)
        {
            var result = new Dictionary<string, string>();
            if (!obj.TryGetProperty("env", out var env)) return result;
            if (env.ValueKind != JsonValueKind.Object)
            {
                Error($"{path}/env", "\"env\" must be an object of strings");
                return result;
            }
            foreach (var member in env.EnumerateObject())
            {
                if (member.Value.ValueKind == JsonValueKind.String) result[member.Name] = member.Value.GetString()!;
                else Error($"{path}/env/{member.Name}", $"environment variable \"{member.Name}\" must be a string");
            }
            return result;
        }

        bool NameOk(string kind, string name, string path)
        {
            if (NamePattern().IsMatch(name)) return true;
            Error(path, $"{kind} name \"{name}\" may contain only letters, digits, \"_\" and \"-\"");
            return false;
        }

        // ───────── ссылки ─────────

        void ServiceRef(string? name, string path)
        {
            if (name != null && !_services.Contains(name)) Error(path, UnknownService(name));
        }

        void ValueRef(string? name, string path)
        {
            if (name != null && !_values.Contains(name))
                Warn(path, $"value \"{name}\" is not declared in \"values\"{Suggest.Hint(name, _values)}; " +
                           "that is fine only if another program sets it with \"explorer-panel set\"");
        }

        void DoRef(JsonElement obj, string path, string key, bool required)
        {
            var name = Str(obj, path, key, required);
            if (name == null) return;
            if (BuiltinActions.TryParse(name, out _, out var service))
            {
                ServiceRef(service, $"{path}/{key}");
                return;
            }
            if (!_actions.Contains(name))
                Error($"{path}/{key}", $"unknown action \"{name}\"{Suggest.Hint(name, _actions.Concat(BuiltinActions.All))}");
        }

        void Icon(JsonElement obj, string path)
        {
            var icon = Str(obj, path, "icon");
            if (icon != null && !LucideIcons.Exists(icon))
                Error($"{path}/icon", $"unknown icon \"{icon}\"{Suggest.Hint(icon, LucideIcons.All)}; use a lucide icon name such as \"play\"");
        }

        // {имя} в тексте должно быть объявленным значением; {date} приходит от календаря и разрешён в действиях
        void Placeholders(string? text, string path, bool allowDate = false)
        {
            if (text == null) return;
            foreach (var name in Template.Names(text))
            {
                if (allowDate && name == "date") continue;
                ValueRef(name, path);
            }
        }

        // ───────── разделы файла ─────────

        public PanelConfig Run(JsonElement root)
        {
            var empty = new PanelConfig("tree", [], [], [], []);
            if (root.ValueKind != JsonValueKind.Object)
            {
                Error("", "the file must contain a JSON object");
                return empty;
            }
            CheckKeys(root, "", "$schema", "version", "scope", "services", "actions", "values", "widgets");

            if (root.TryGetProperty("version", out var version)
                && !(version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int number) && number == 1))
                Error("/version", $"unsupported version {version.GetRawText()} (this program understands version 1)");

            string scope = "tree";
            if (root.TryGetProperty("scope", out var scopeValue))
            {
                if (scopeValue.ValueKind == JsonValueKind.String && scopeValue.GetString() is "tree" or "folder") scope = scopeValue.GetString()!;
                else Error("/scope", "\"scope\" must be \"tree\" or \"folder\"");
            }

            CollectNames(root, "services", _services);
            CollectNames(root, "actions", _actions);
            CollectNames(root, "values", _values);

            var services = ReadServices(root);
            var actions = ReadActions(root);
            var values = ReadValues(root);
            var widgets = root.TryGetProperty("widgets", out var widgetsValue)
                ? ReadWidgets(widgetsValue, "/widgets")
                : DefaultWidgets(services, actions);
            return new PanelConfig(scope, services, actions, values, widgets);
        }

        static void CollectNames(JsonElement root, string section, HashSet<string> into)
        {
            if (root.TryGetProperty(section, out var value) && value.ValueKind == JsonValueKind.Object)
                foreach (var member in value.EnumerateObject()) into.Add(member.Name);
        }

        IEnumerable<(string Name, JsonElement Value, string Path)> Section(JsonElement root, string section, string kind)
        {
            if (!root.TryGetProperty(section, out var value)) yield break;
            if (value.ValueKind != JsonValueKind.Object)
            {
                Error($"/{section}", $"\"{section}\" must be an object");
                yield break;
            }
            foreach (var member in value.EnumerateObject())
            {
                var path = $"/{section}/{member.Name}";
                if (!NameOk(kind, member.Name, path)) continue;
                if (member.Value.ValueKind != JsonValueKind.Object)
                {
                    Error(path, $"{kind} \"{member.Name}\" must be an object");
                    continue;
                }
                yield return (member.Name, member.Value, path);
            }
        }

        List<ServiceDef> ReadServices(JsonElement root)
        {
            var result = new List<ServiceDef>();
            foreach (var (name, value, path) in Section(root, "services", "service"))
            {
                CheckKeys(value, path, "run", "cwd", "env", "health", "stop");
                var run = Str(value, path, "run", required: true);
                var cwd = Cwd(value, path);
                var env = Env(value, path);
                var stop = Str(value, path, "stop");
                Placeholders(run, $"{path}/run");
                Placeholders(stop, $"{path}/stop");

                HealthDef? health = null;
                bool healthGiven = value.TryGetProperty("health", out var healthValue);
                if (healthGiven)
                {
                    const string expected = "\"health\" must be an http(s) address or { \"run\": \"command\" }";
                    if (healthValue.ValueKind == JsonValueKind.String)
                    {
                        if (IsHttpUrl(healthValue.GetString()!)) health = new HealthDef(healthValue.GetString(), null);
                        else Error($"{path}/health", expected);
                    }
                    else if (healthValue.ValueKind == JsonValueKind.Object)
                    {
                        CheckKeys(healthValue, $"{path}/health", "run");
                        if (Str(healthValue, $"{path}/health", "run", required: true) is { } healthRun) health = new HealthDef(null, healthRun);
                    }
                    else Error($"{path}/health", expected);
                }
                if (stop != null && !healthGiven)
                    Warn(path, $"service \"{name}\" has \"stop\" but no \"health\": its status cannot be known after the start command exits");

                if (run != null) result.Add(new ServiceDef(name, run, cwd, env, health, stop));
            }
            return result;
        }

        List<ActionDef> ReadActions(JsonElement root)
        {
            var result = new List<ActionDef>();
            foreach (var (name, value, path) in Section(root, "actions", "action"))
            {
                if (BuiltinActions.IsBuiltin(name))
                {
                    Error(path, $"\"{name}\" is a built-in action and cannot be redefined");
                    continue;
                }
                CheckKeys(value, path, "run", "open", "cwd", "env", "needs");
                bool hasRun = value.TryGetProperty("run", out _), hasOpen = value.TryGetProperty("open", out _);
                if (hasRun == hasOpen)
                {
                    Error(path, $"action \"{name}\" needs exactly one of \"run\" or \"open\"");
                    continue;
                }
                var run = Str(value, path, "run");
                var open = Str(value, path, "open");
                Placeholders(run, $"{path}/run", allowDate: true);
                Placeholders(open, $"{path}/open", allowDate: true);
                var cwd = Cwd(value, path);
                var env = Env(value, path);

                var needs = new List<string>();
                if (value.TryGetProperty("needs", out var needsValue))
                {
                    if (needsValue.ValueKind != JsonValueKind.Array) Error($"{path}/needs", "\"needs\" must be an array of service names");
                    else
                    {
                        int i = 0;
                        foreach (var item in needsValue.EnumerateArray())
                        {
                            var itemPath = $"{path}/needs/{i++}";
                            if (item.ValueKind != JsonValueKind.String) Error(itemPath, "\"needs\" must be an array of service names");
                            else if (!_services.Contains(item.GetString()!)) Error(itemPath, UnknownService(item.GetString()!));
                            else needs.Add(item.GetString()!);
                        }
                    }
                }
                if (run != null || open != null) result.Add(new ActionDef(name, run, open, cwd, env, needs));
            }
            return result;
        }

        List<ValueDef> ReadValues(JsonElement root)
        {
            var result = new List<ValueDef>();
            foreach (var (name, value, path) in Section(root, "values", "value"))
            {
                CheckKeys(value, path, "run", "every", "cwd", "default");
                var run = Str(value, path, "run");
                var cwd = Cwd(value, path);
                double every = PanelConfig.DefaultEvery;
                if (value.TryGetProperty("every", out var everyValue))
                {
                    if (everyValue.ValueKind == JsonValueKind.Number && everyValue.GetDouble() > 0) every = everyValue.GetDouble();
                    else Error($"{path}/every", "\"every\" must be a number greater than 0 (seconds)");
                }
                JsonNode? @default = value.TryGetProperty("default", out var defaultValue)
                    ? JsonNode.Parse(defaultValue.GetRawText(), documentOptions: DocumentOptions)
                    : null;
                result.Add(new ValueDef(name, run, every, cwd, @default));
            }
            return result;
        }

        // ───────── виджеты ─────────

        public JsonArray ReadWidgets(JsonElement widgets, string basePath)
        {
            var result = new JsonArray();
            if (widgets.ValueKind != JsonValueKind.Array)
            {
                Error(basePath, "\"widgets\" must be an array");
                return result;
            }
            int i = 0;
            foreach (var widget in widgets.EnumerateArray())
            {
                var path = $"{basePath}/{i++}";
                if (CheckWidget(widget, path)) result.Add(JsonNode.Parse(widget.GetRawText(), documentOptions: DocumentOptions));
            }
            return result;
        }

        bool CheckWidget(JsonElement widget, string path)
        {
            if (widget.ValueKind != JsonValueKind.Object)
            {
                Error(path, "a widget must be an object");
                return false;
            }
            var type = Str(widget, path, "type", required: true);
            if (type == null) return false;
            if (!WidgetTypes.All.Contains(type))
            {
                Error($"{path}/type", $"unknown widget type \"{type}\"{Suggest.Hint(type, WidgetTypes.All)}");
                return false;
            }

            int errorsBefore = _errors;
            void Keys(params string[] own) => CheckKeys(widget, path, ["type", "pin", "tooltip", .. own]);
            void RequiredValue() => ValueRef(Str(widget, path, "value", required: true), $"{path}/value");
            void IconOrLabel(string what)
            {
                Icon(widget, path);
                Placeholders(Str(widget, path, "label"), $"{path}/label");
                if (!widget.TryGetProperty("icon", out _) && !widget.TryGetProperty("label", out _))
                    Error(path, $"a {what} needs \"icon\" or \"label\"");
            }

            switch (type)
            {
                case WidgetTypes.Status:
                    Keys("service", "value", "label");
                    bool hasService = widget.TryGetProperty("service", out _), hasValue = widget.TryGetProperty("value", out _);
                    if (hasService == hasValue) Error(path, "a status widget needs exactly one of \"service\" or \"value\"");
                    else if (hasService) ServiceRef(Str(widget, path, "service"), $"{path}/service");
                    else ValueRef(Str(widget, path, "value"), $"{path}/value");
                    Str(widget, path, "label");
                    break;

                case WidgetTypes.Button:
                    Keys("icon", "label", "do");
                    IconOrLabel("button");
                    DoRef(widget, path, "do", required: true);
                    break;

                case WidgetTypes.Text:
                    Keys("text", "icon");
                    Icon(widget, path);
                    Placeholders(Str(widget, path, "text", required: true), $"{path}/text");
                    break;

                case WidgetTypes.Toggle:
                    Keys("value", "label", "on", "off");
                    RequiredValue();
                    Str(widget, path, "label");
                    DoRef(widget, path, "on", required: false);
                    DoRef(widget, path, "off", required: false);
                    break;

                case WidgetTypes.Select:
                    Keys("value", "options", "do");
                    RequiredValue();
                    if (!widget.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array
                        || options.GetArrayLength() == 0 || options.EnumerateArray().Any(o => o.ValueKind != JsonValueKind.String))
                        Error($"{path}/options", "\"options\" must be a non-empty array of strings");
                    DoRef(widget, path, "do", required: false);
                    break;

                case WidgetTypes.Progress:
                    Keys("value", "label");
                    RequiredValue();
                    Str(widget, path, "label");
                    break;

                case WidgetTypes.Menu:
                    Keys("icon", "label", "items");
                    IconOrLabel("menu");
                    if (!widget.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                    {
                        Error($"{path}/items", "\"items\" must be a non-empty array of { \"label\", \"do\" }");
                        break;
                    }
                    int index = 0;
                    foreach (var item in items.EnumerateArray())
                    {
                        var itemPath = $"{path}/items/{index++}";
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            Error(itemPath, "a menu item must be an object with \"label\" and \"do\"");
                            continue;
                        }
                        CheckKeys(item, itemPath, "label", "do", "icon");
                        Str(item, itemPath, "label", required: true);
                        Icon(item, itemPath);
                        DoRef(item, itemPath, "do", required: true);
                    }
                    break;

                case WidgetTypes.Clock:
                    Keys("seconds");
                    Bool(widget, path, "seconds");
                    break;

                case WidgetTypes.Calendar:
                    Keys("do", "icon");
                    Icon(widget, path);
                    DoRef(widget, path, "do", required: true);
                    break;

                case WidgetTypes.Slot:
                    Keys("value");
                    RequiredValue();
                    break;

                default: // stopwatch, separator
                    Keys();
                    break;
            }

            Bool(widget, path, "pin");
            Placeholders(Str(widget, path, "tooltip"), $"{path}/tooltip");
            return _errors == errorsBefore;
        }

        // Панель по умолчанию, когда "widgets" в файле нет: статусы, запуск и остановка, кнопка на каждое действие, логи.
        static JsonArray DefaultWidgets(List<ServiceDef> services, List<ActionDef> actions)
        {
            var result = new JsonArray();
            foreach (var service in services)
                result.Add(new JsonObject { ["type"] = WidgetTypes.Status, ["service"] = service.Name });
            if (services.Count > 0)
            {
                result.Add(Button("play", "Start", BuiltinActions.Start, pin: true));
                result.Add(Button("square", "Stop", BuiltinActions.Stop, pin: true));
            }
            foreach (var action in actions)
            {
                string icon = action.Run != null ? "terminal" : IsHttpUrl(action.Open!) ? "globe" : "folder-open";
                result.Add(Button(icon, action.Name, action.Name, pin: false));
            }
            if (services.Count > 0) result.Add(Button("file-text", "Logs", BuiltinActions.Logs, pin: false));
            return result;

            static JsonObject Button(string icon, string tooltip, string action, bool pin)
            {
                var button = new JsonObject { ["type"] = WidgetTypes.Button, ["icon"] = icon, ["tooltip"] = tooltip, ["do"] = action };
                if (pin) button["pin"] = true;
                return button;
            }
        }
    }
}
