using ExplorerPanel.Core;
using ExplorerPanel.Core.Api;

namespace ExplorerPanel.Cli;

/// <summary>
/// <c>explorer-panel check</c>: проверяет panel.json и говорит, что с панелью сейчас.
/// Вывод рассчитан на агента: по строке на проблему в виде <c>файл:строка:столбец: error|warning: сообщение</c>.
/// </summary>
static class CheckCommand
{
    static string Count(int number, string noun) => $"{number} {noun}{(number == 1 ? "" : "s")}";

    /// <param name="hostStatus">Что видит работающая фоновая программа; null — она не запущена.</param>
    /// <returns>0 — ошибок нет; 1 — есть ошибки или панели нет.</returns>
    public static int Run(string folder, TextWriter output, TrustStore trust, StatusResponse? hostStatus)
    {
        var root = ProjectLocator.FindRoot(Path.GetFullPath(folder));
        if (root == null)
        {
            output.WriteLine($"error: no {PanelLoader.DirName}/{PanelLoader.FileName} found in {Path.GetFullPath(folder)} or its parent folders");
            return 1;
        }

        var loaded = PanelLoader.LoadFile(root);
        foreach (var problem in loaded.Problems) output.WriteLine(problem);

        int errors = loaded.Problems.Count(p => p.IsError), warnings = loaded.Problems.Count - errors;
        if (loaded.Config is { } config)
        {
            var summary = $"OK: {Count(config.Services.Count, "service")}, {Count(config.Actions.Count, "action")}, "
                          + $"{Count(config.Values.Count, "value")}, {Count(config.Widgets.Count, "widget")}";
            output.WriteLine(warnings > 0 ? $"{summary} ({Count(warnings, "warning")})" : summary);
        }
        else output.WriteLine($"FAILED: {Count(errors, "error")}, {Count(warnings, "warning")}");

        output.WriteLine(hostStatus != null ? "host: running" : "host: not running");
        output.WriteLine(trust.Get(root) switch
        {
            true => "trusted: yes",
            false => "trusted: no (blocked; run: explorer-panel trust)",
            null => "trusted: not decided (run: explorer-panel trust)",
        });
        if (hostStatus != null)
        {
            output.WriteLine(hostStatus.Loaded ? "loaded: yes" : "loaded: no (the panel shows the last valid version, if there was one)");
            if (!string.IsNullOrEmpty(hostStatus.LastError)) output.WriteLine("last panel error: " + hostStatus.LastError);
        }
        return errors > 0 ? 1 : 0;
    }
}
