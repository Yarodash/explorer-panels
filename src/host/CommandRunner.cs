using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerPanel.Host;

sealed record CommandResult(int ExitCode, string Output);

/// <summary>
/// Выполняет команды из panel.json так же, как если бы их набрали в командной строке Windows: через cmd.exe.
/// Окно не показывается.
/// </summary>
static class CommandRunner
{
    [DllImport("kernel32.dll")] static extern uint GetOEMCP();

    // Программы (python, node, git) пишут вывод в UTF-8, а сам cmd — в старой кодировке консоли (в русской Windows это 866).
    // Поэтому каждая строка читается как UTF-8, а если это не UTF-8 — как текст в кодировке консоли.
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static readonly Encoding ConsoleEncoding = CreateConsoleEncoding();

    static Encoding CreateConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Encoding.Latin1; }
    }

    static string Decode(byte[] buffer, int count)
    {
        while (count > 0 && buffer[count - 1] is (byte)'\r' or (byte)'\n') count--;
        try { return StrictUtf8.GetString(buffer, 0, count); }
        catch (DecoderFallbackException) { return ConsoleEncoding.GetString(buffer, 0, count); }
    }

    static ProcessStartInfo StartInfo(string command, string cwd, IReadOnlyDictionary<string, string> env)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec ? comSpec : "cmd.exe")
        {
            // /d — без автозапуска из реестра; /s /c "…" — команда берётся как есть, вместе с её собственными кавычками
            Arguments = $"/d /s /c \"{command}\"",
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        // чтобы вывод серверов приходил сразу, в UTF-8 и без управляющих символов цвета
        // (если переменная уже задана — в panel.json или унаследована, — её не трогаем)
        foreach (var (name, value) in env) info.Environment[name] = value;
        info.Environment.TryAdd("PYTHONUNBUFFERED", "1");
        info.Environment.TryAdd("PYTHONIOENCODING", "utf-8");
        info.Environment.TryAdd("NO_COLOR", "1");
        info.Environment.TryAdd("FORCE_COLOR", "0");
        return info;
    }

    // Читает поток вывода построчно и отдаёт каждую строку в onLine.
    static async Task Pump(Stream stream, Action<string> onLine)
    {
        const int MaxLine = 64 * 1024; // строка без переводов (например, полоса загрузки) режется на части
        var chunk = new byte[8192];
        var line = new byte[MaxLine];
        int length = 0;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    line[length++] = chunk[i];
                    if (chunk[i] != (byte)'\n' && length < MaxLine) continue;
                    onLine(Decode(line, length));
                    length = 0;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // процесс завершён, поток закрыт
        }
        if (length > 0) onLine(Decode(line, length));
    }

    /// <summary>Запускает команду; вторая часть результата завершается, когда весь вывод дочитан.</summary>
    internal static (Process Process, Task Output) StartCore(string command, string cwd, IReadOnlyDictionary<string, string> env, Action<string> onLine)
    {
        var process = new Process { StartInfo = StartInfo(command, cwd, env), EnableRaisingEvents = true };
        process.Start();
        process.StandardInput.Close(); // команда, которая вдруг спросит что-то с клавиатуры, получит «конец ввода», а не зависнет
        var output = Task.WhenAll(
            Task.Run(() => Pump(process.StandardOutput.BaseStream, onLine)),
            Task.Run(() => Pump(process.StandardError.BaseStream, onLine)));
        return (process, output);
    }

    /// <summary>Запускает долгоживущую команду. Каждая строка её вывода передаётся в <paramref name="onLine"/>.</summary>
    public static Process Start(string command, string cwd, IReadOnlyDictionary<string, string> env, Action<string> onLine) =>
        StartCore(command, cwd, env, onLine).Process;

    /// <summary>Выполняет команду до конца и возвращает код возврата и весь вывод. По истечении времени завершает её.</summary>
    public static async Task<CommandResult> RunAsync(string command, string cwd, IReadOnlyDictionary<string, string> env, TimeSpan timeout)
    {
        var text = new StringBuilder();
        void Add(string line) { lock (text) text.AppendLine(line); }

        var (process, output) = StartCore(command, cwd, env, Add);
        using (process)
        {
            using var cancel = new CancellationTokenSource(timeout);
            int exitCode;
            try
            {
                await process.WaitForExitAsync(cancel.Token);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                ProcessInfo.KillTree(process.Id);
                Add($"(timed out after {timeout.TotalSeconds:0} s and was stopped)");
                exitCode = -1;
            }
            // Дочитываем вывод. Если команда оставила работать фоновый процесс, он держит поток открытым — долго не ждём.
            await Task.WhenAny(output, Task.Delay(2000));
            lock (text) return new CommandResult(exitCode, text.ToString());
        }
    }
}
