using ExplorerPanel.Core;

namespace ExplorerPanel.Host;

/// <summary>Журнал самой фоновой программы (не серверов): %LOCALAPPDATA%\ExplorerPanels\host.log.</summary>
static class Log
{
    const long MaxBytes = 2_000_000;
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            var file = AppPaths.HostLog;
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytes) File.Move(file, file + ".1", overwrite: true); // один старый файл про запас
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // журнал не должен ронять программу
        }
    }
}
