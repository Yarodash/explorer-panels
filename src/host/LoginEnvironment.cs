using System.Runtime.InteropServices;

namespace ExplorerPanel.Host;

/// <summary>
/// Окружение, с которым программа запускает всё остальное: серверы, действия, терминал с Claude.
///
/// Программу может запустить кто угодно — автозапуск при входе в систему или, например, установщик из терминала
/// внутри Claude Code либо Git Bash. Во втором случае ей достаются переменные того терминала: метки сеанса Claude
/// (из-за них Claude, открытый кнопкой, считал себя вложенным сеансом), PATH с добавками Git Bash и прочее.
/// Поэтому при запуске программа заменяет своё окружение тем, которое Windows даёт при обычном входе в систему.
/// Постоянные настройки пользователя (заданные в «Переменных среды») при этом сохраняются: они там есть.
/// </summary>
static class LoginEnvironment
{
    /// <summary>Переменные самой программы (ими управляются проверочные экземпляры) не трогаются.</summary>
    const string OwnPrefix = "EXPLORER_PANEL_";

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    const uint TOKEN_DUPLICATE = 0x0002, TOKEN_IMPERSONATE = 0x0004, TOKEN_QUERY = 0x0008;

    /// <summary>
    /// Переменные окружения, какими их получает программа, запущенная при входе пользователя в систему.
    /// null — узнать не удалось.
    /// </summary>
    public static Dictionary<string, string>? Read()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE, out var token)) return null;
        try
        {
            // inherit: false — без переменных текущего процесса, только то, что задано для пользователя и системы
            if (!CreateEnvironmentBlock(out var block, token, inherit: false)) return null;
            try
            {
                // блок — строки "имя=значение", каждая завершена нулём; после последней — ещё один ноль
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var at = block; ;)
                {
                    var entry = Marshal.PtrToStringUni(at);
                    if (string.IsNullOrEmpty(entry)) break;
                    at += (entry.Length + 1) * sizeof(char);
                    int equals = entry.IndexOf('=', 1); // записи вида "=C:=C:\папка" — служебные, не переменные
                    if (equals > 0 && entry[0] != '=') result[entry[..equals]] = entry[(equals + 1)..];
                }
                return result;
            }
            finally { DestroyEnvironmentBlock(block); }
        }
        finally { CloseHandle(token); }
    }

    /// <summary>Что изменить в текущем окружении, чтобы оно стало таким, как при входе в систему.</summary>
    public static (List<string> Remove, IReadOnlyDictionary<string, string> Set) Plan(
        IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> fresh)
    {
        var known = new HashSet<string>(fresh.Keys, StringComparer.OrdinalIgnoreCase);
        var remove = current.Keys
            .Where(name => !known.Contains(name) && !name.StartsWith(OwnPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (remove, fresh);
    }

    /// <summary>Заменяет окружение этого процесса окружением входа в систему. Вызывается один раз при запуске.</summary>
    public static void Apply()
    {
        try
        {
            if (Read() is not { Count: > 0 } fresh)
            {
                Log.Write("the login environment is not available; keeping the inherited one");
                return;
            }
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                current[(string)entry.Key] = (string?)entry.Value ?? "";

            var (remove, set) = Plan(current, fresh);
            foreach (var name in remove) Environment.SetEnvironmentVariable(name, null);
            foreach (var (name, value) in set) Environment.SetEnvironmentVariable(name, value);
            if (remove.Count > 0) Log.Write($"environment: dropped {remove.Count} variables inherited from the launching program");
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Write("environment was left as inherited: " + ex.Message);
        }
    }
}
