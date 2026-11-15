using System.Security.Cryptography;
using System.Text;

namespace ExplorerPanel.Core;

/// <summary>Где программа хранит свои данные.</summary>
public static class AppPaths
{
    /// <summary>Переменная окружения, которой можно переназначить папку данных (используется в проверках).</summary>
    public const string DataDirVariable = "EXPLORER_PANEL_DATA";

    public static string DataDir =>
        Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExplorerPanels");

    /// <summary>Разрешённые и запрещённые папки.</summary>
    public static string TrustFile => Path.Combine(DataDir, "trust.json");

    /// <summary>Порт и номер процесса работающей фоновой программы — по нему её находит команда explorer-panel.</summary>
    public static string HostInfoFile => Path.Combine(DataDir, "host.json");

    public static string HostLog => Path.Combine(DataDir, "host.log");

    /// <summary>Сохранённое состояние проекта: запущенные панелью процессы, значения тумблеров.</summary>
    public static string StateFile(string projectRoot) => StateFileIn(DataDir, projectRoot);

    public static string StateFileIn(string dataDir, string projectRoot)
    {
        var key = ProjectLocator.Normalize(projectRoot).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return Path.Combine(dataDir, "state", hash + ".json");
    }
}
