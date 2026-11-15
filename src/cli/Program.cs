using ExplorerPanel.Core;

namespace ExplorerPanel.Cli;

static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("explorer-panel: " + ex.Message);
            return 2;
        }
    }

    static int Run(string[] args)
    {
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        switch (command)
        {
            case "version" or "--version" or "-v":
                Console.WriteLine($"explorer-panel {AppInfo.Version}");
                return 0;

            case "install":
                // автозапуск фоновой программы и команда explorer-panel в PATH; папка — та, где лежит эта программа
                InstallCommand.RegisterForCurrentUser(AppContext.BaseDirectory);
                Console.WriteLine($"registered: starts at sign-in, 'explorer-panel' is on PATH ({Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)})");
                Console.WriteLine("open a new terminal to use the command");
                return 0;

            case "uninstall":
                InstallCommand.UnregisterForCurrentUser(AppContext.BaseDirectory);
                Console.WriteLine("unregistered: no autostart, removed from PATH");
                return 0;

            default:
                return Commands.Run(command, args.Skip(1).ToArray());
        }
    }
}
