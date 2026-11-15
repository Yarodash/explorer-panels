using System.Text.Json;
using System.Text.Json.Nodes;
using ExplorerPanel.Core;
using ExplorerPanel.Core.Api;

namespace ExplorerPanel.Cli;

/// <summary>Команды для работы с панелями. Папка по умолчанию — текущая; корень проекта ищется вверх по дереву.</summary>
static class Commands
{
    public const string Usage = """
        explorer-panel — control panels inside Windows File Explorer

        usage: explorer-panel <command> [arguments] [folder]

        A project is a folder with .explorer_components/panel.json. [folder] defaults to the
        current folder; the project is found by walking up from it.

          check                    validate panel.json; one line per problem, exit code 1 on errors
          trust | untrust          allow this folder's panel to run commands | forget the decision
          status                   services and values of the project, as JSON
          start | stop | restart [service]
                                   control all services or one of them
          do <action>              run an action from panel.json (or a built-in one, e.g. logs)
          set name=value ...       set values instantly (plain text or JSON)
          logs                     print the log file path and its last lines

          version                  print the version
          install | uninstall      start at sign-in and put 'explorer-panel' on PATH | undo that

        Exit codes: 0 ok, 1 failed, 2 wrong usage, 3 the background program is not running.
        """;

    const int Ok = 0, Failed = 1, WrongUsage = 2, NoHost = 3;

    public static int Run(string command, string[] args)
    {
        switch (command)
        {
            case "help" or "--help" or "-h":
                Console.WriteLine(Usage);
                return Ok;

            case "check":
            {
                var folder = Folder(command, args, out _);
                using var host = Connect();
                StatusResponse? status = null;
                try
                {
                    if (host != null) status = host.StatusAsync(folder).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
                return CheckCommand.Run(folder, Console.Out, new TrustStore(AppPaths.TrustFile), status);
            }

            case "trust" or "untrust":
            {
                // работает и без запущенной программы: она сама замечает изменение файла разрешений
                var folder = Folder(command, args, out _);
                if (ProjectLocator.FindRoot(folder) is not { } root) return NoPanel(folder);
                new TrustStore(AppPaths.TrustFile).Set(root, command == "trust" ? true : null);
                Console.WriteLine(command == "trust" ? $"allowed: {root}" : $"forgotten: {root} (the panel will ask again)");
                return Ok;
            }

            case "status":
            {
                var folder = Folder(command, args, out _);
                using var host = Connect();
                if (host == null) return HostMissing();
                var status = host.StatusAsync(folder).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions(ApiProtocol.Json) { WriteIndented = true }));
                return status.Root == null ? Failed : Ok;
            }

            case "start" or "stop" or "restart":
            {
                var folder = Folder(command, args, out var rest);
                return Do(folder, rest.Count > 0 ? $"{command}:{rest[0]}" : command);
            }

            case "do":
            {
                var folder = Folder(command, args, out var rest);
                if (rest.Count != 1) return Wrong("usage: explorer-panel do <action> [folder]");
                return Do(folder, rest[0]);
            }

            case "set":
            {
                var folder = Folder(command, args, out var rest);
                var values = new JsonObject();
                foreach (var argument in rest)
                {
                    if (!TryParseValue(argument, out var name, out var value)) return Wrong($"expected name=value, got \"{argument}\"");
                    values[name] = value;
                }
                if (values.Count == 0) return Wrong("usage: explorer-panel set name=value ... [folder]");
                using var host = Connect();
                if (host == null) return HostMissing();
                return Report(host.SetAsync(folder, values).GetAwaiter().GetResult());
            }

            case "logs":
            {
                var folder = Folder(command, args, out _);
                if (ProjectLocator.FindRoot(folder) is not { } root) return NoPanel(folder);
                var file = Path.Combine(root, PanelLoader.DirName, "logs", "services.log");
                Console.WriteLine(file);
                if (!File.Exists(file))
                {
                    Console.WriteLine("(nothing has been started from the panel yet)");
                    return Ok;
                }
                foreach (var line in File.ReadLines(file).TakeLast(40)) Console.WriteLine(line);
                return Ok;
            }

            default:
                return Wrong($"unknown command \"{command}\"\n\n{Usage}");
        }
    }

    // ───────── разбор аргументов ─────────

    static string Folder(string command, string[] args, out List<string> rest) =>
        SplitArgs(command, args, Directory.GetCurrentDirectory(), out rest);

    // сколько обязательных и необязательных слов у команды до папки
    static (int Required, int Optional) Words(string command) => command switch
    {
        "do" => (1, 0),
        "start" or "stop" or "restart" => (0, 1),
        "set" => (0, int.MaxValue),
        _ => (0, 0),
    };

    // Похоже ли слово на путь, а не на имя сервера: имена состоят из букв, цифр, "_" и "-".
    static bool LooksLikePath(string word) => word.IndexOfAny(['.', '\\', '/', ':']) >= 0;

    /// <summary>
    /// Отделяет папку проекта от остальных аргументов. Папка задаётся <c>--folder путь</c> либо последним
    /// аргументом сверх слов самой команды; иначе это <paramref name="cwd"/>.
    /// Имя действия или сервера никогда не принимается за папку, даже если папка с таким именем существует:
    /// <c>do tests</c> — это действие tests, а не папка tests.
    /// </summary>
    public static string SplitArgs(string command, string[] args, string cwd, out List<string> rest)
    {
        rest = args.ToList();
        string Full(string path) => Path.GetFullPath(Path.Combine(cwd, path));

        int option = rest.IndexOf("--folder");
        if (option >= 0 && option + 1 < rest.Count)
        {
            var given = rest[option + 1];
            rest.RemoveRange(option, 2);
            return Full(given);
        }

        var (required, optional) = Words(command);
        if (command == "set")
        {
            // значения — это имя=значение; последнее слово без "=" — папка
            if (rest.Count > 0 && !rest[^1].Contains('='))
            {
                var given = rest[^1];
                rest.RemoveAt(rest.Count - 1);
                return Full(given);
            }
            return cwd;
        }

        // лишнее слово сверх всех слов команды — папка
        if (rest.Count > required + optional)
        {
            var given = rest[^1];
            rest.RemoveAt(rest.Count - 1);
            return Full(given);
        }
        // у start/stop/restart единственное слово — имя сервера, если только оно не выглядит как путь
        if (optional == 1 && rest.Count == required + 1 && LooksLikePath(rest[^1]))
        {
            var given = rest[^1];
            rest.RemoveAt(rest.Count - 1);
            return Full(given);
        }
        return cwd;
    }

    /// <summary>
    /// Разбирает <c>имя=значение</c>. Значение — JSON, если это правильный JSON (число, true, объект), иначе текст.
    /// </summary>
    public static bool TryParseValue(string argument, out string name, out JsonNode? value)
    {
        int at = argument.IndexOf('=');
        name = at > 0 ? argument[..at] : "";
        value = null;
        if (at <= 0) return false;
        var text = argument[(at + 1)..];
        try { value = JsonNode.Parse(text); }
        catch (JsonException) { value = JsonValue.Create(text); }
        value ??= JsonValue.Create(text); // "null" и пустая строка остаются текстом
        return true;
    }

    // ───────── общение с программой ─────────

    static HostClient? Connect() => HostClient.TryConnectAsync().GetAwaiter().GetResult();

    static int Do(string folder, string action)
    {
        using var host = Connect();
        if (host == null) return HostMissing();
        return Report(host.DoAsync(folder, action).GetAwaiter().GetResult());
    }

    static int Report(ApiResult result)
    {
        if (result.Ok)
        {
            Console.WriteLine("ok");
            return Ok;
        }
        Console.Error.WriteLine("failed: " + result.Error);
        return Failed;
    }

    static int HostMissing()
    {
        Console.Error.WriteLine("explorer-panel: the background program is not running (start explorer-panel-host.exe, or install it with scripts/install.ps1)");
        return NoHost;
    }

    static int NoPanel(string folder)
    {
        Console.Error.WriteLine($"explorer-panel: no {PanelLoader.DirName}/{PanelLoader.FileName} found in {folder} or its parent folders");
        return Failed;
    }

    static int Wrong(string message)
    {
        Console.Error.WriteLine("explorer-panel: " + message);
        return WrongUsage;
    }
}
