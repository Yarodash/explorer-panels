using System.Security.Cryptography;
using System.Text;
using ExplorerPanel.Core;

namespace ExplorerPanel.Host;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // запуск в роли посредника между сервером и его логом (см. Pump)
        if (Pump.TryRun(args, out int exitCode)) return exitCode;
        RunHost();
        return 0;
    }

    static void RunHost()
    {
        // Один экземпляр на пользователя. У проверочных экземпляров своя папка данных — и своё имя,
        // чтобы они не мешали установленной программе.
        using var single = new Mutex(initiallyOwned: true, MutexName(), out bool first);
        if (!first) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("unhandled: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);

        using var context = new HostContext();
        Application.Run(context);
        Log.Write("stopped");
    }

    static string MutexName()
    {
        const string name = @"Local\ExplorerPanelsHost";
        if (Environment.GetEnvironmentVariable(AppPaths.DataDirVariable) is not { Length: > 0 } custom) return name;
        return name + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(custom.ToLowerInvariant())))[..12];
    }
}
