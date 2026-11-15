namespace ExplorerPanel.Cli;

/// <summary>Команды для работы с панелями (появляются в задаче 8).</summary>
static class Commands
{
    public const string Usage = """
        explorer-panel — control panels inside Windows File Explorer

        usage: explorer-panel <command> [folder]

          version      print the version
          install      start at sign-in and put 'explorer-panel' on PATH
          uninstall    undo 'install'
        """;

    public static int Run(string command, string[] args)
    {
        if (command is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }
        Console.Error.WriteLine($"explorer-panel: unknown command \"{command}\"\n");
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
