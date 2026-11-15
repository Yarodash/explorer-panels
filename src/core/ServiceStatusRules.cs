namespace ExplorerPanel.Core;

public enum ServiceStatus { Stopped, Starting, Running, Failed }

/// <summary>Кто слушает порт из проверочного адреса сервера.</summary>
public enum PortOwner
{
    /// <summary>Никто, либо у сервера нет местного проверочного адреса.</summary>
    None,
    /// <summary>Процесс этого проекта: запущен панелью или работает из папки проекта.</summary>
    Project,
    /// <summary>Посторонняя программа (например, другой проект на том же порту).</summary>
    Foreign,
}

/// <summary>Что известно о сервере в данный момент.</summary>
/// <param name="HasHealth">У сервера задана проверка «жив ли».</param>
/// <param name="Healthy">Последняя проверка прошла.</param>
/// <param name="OwnProcessAlive">Процесс, запущенный панелью, ещё работает.</param>
/// <param name="ExitCode">Код, с которым завершился запущенный панелью процесс; null — не запускался или ещё работает.</param>
/// <param name="SecondsSinceExit">Сколько секунд назад он завершился.</param>
/// <param name="WantRunning">Владелец нажал «запустить» и с тех пор не нажимал «выключить».</param>
public sealed record ServiceObservation(
    bool HasHealth, bool Healthy, bool OwnProcessAlive, int? ExitCode, double SecondsSinceExit,
    bool WantRunning, PortOwner PortOwner, string? ForeignName, int? ForeignPid);

/// <summary>Единственное место, где решается, какой статус показать. Статус не должен врать.</summary>
public static class ServiceStatusRules
{
    // программы, которые слушают порт от имени сервера, работающего в Docker или WSL
    static readonly HashSet<string> PortForwarders = new(StringComparer.OrdinalIgnoreCase)
    {
        "wslrelay", "wslhost", "com.docker.backend", "docker-proxy", "vpnkit", "gvproxy",
    };

    /// <summary>
    /// Слушает ли программа с таким именем порт «за другого». Порт сервера в Docker или WSL на самом деле
    /// открывает их посредник; считать его посторонней программой, занявшей порт, нельзя.
    /// </summary>
    public static bool IsPortForwarder(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        return PortForwarders.Contains(name);
    }

    /// <summary>
    /// Сколько ждать ответа после того, как команда-запускалка (например, <c>docker compose up -d</c>)
    /// успешно завершилась.
    /// </summary>
    public const double LauncherGraceSeconds = 60;

    public static (ServiceStatus Status, string? Detail) Compute(ServiceObservation o)
    {
        // Порт занят чужой программой: отвечает не наш сервер, поэтому «работает» было бы неправдой.
        if (o.PortOwner == PortOwner.Foreign)
            return (ServiceStatus.Failed, $"port in use by another program: {o.ForeignName ?? "unknown"} ({o.ForeignPid})");

        if (o.HasHealth)
        {
            if (o.Healthy) return (ServiceStatus.Running, null);
            if (o.OwnProcessAlive) return (ServiceStatus.Starting, null);
            if (o.WantRunning && o.ExitCode == 0)
                return o.SecondsSinceExit <= LauncherGraceSeconds
                    ? (ServiceStatus.Starting, null)
                    : (ServiceStatus.Failed, "the start command finished but the service did not start responding");
        }
        else if (o.OwnProcessAlive)
        {
            return (ServiceStatus.Running, null); // без проверки «работает» означает «процесс жив»
        }

        // процесс завершился сам, хотя его не останавливали
        if (o.WantRunning && o.ExitCode is { } code)
            return (ServiceStatus.Failed, code == 0 ? "the process exited" : $"exited with code {code}");

        return (ServiceStatus.Stopped, null);
    }
}
