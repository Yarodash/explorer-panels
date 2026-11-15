using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerPanel.Host;

/// <summary>Сведения о чужих процессах: кто слушает порт, из какой папки работает, кто чей родитель.</summary>
static class ProcessInfo
{
    // ───────── кто слушает порт ─────────

    const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_LISTENER = 3;

    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(byte[]? table, ref int size, bool sort, int family, int tableClass, uint reserved);

    /// <summary>Номера процессов, которые слушают порт (на любом адресе, IPv4 и IPv6).</summary>
    public static IReadOnlyList<int> ListeningPids(int port)
    {
        var result = new List<int>();
        // строка таблицы IPv4: состояние, адрес, порт, удалённый адрес, удалённый порт, процесс — по 4 байта
        Collect(AF_INET, rowSize: 24, portOffset: 8, pidOffset: 20);
        // строка таблицы IPv6: адрес (16), область (4), порт (4), удалённый адрес (16), область (4), порт (4), состояние (4), процесс (4)
        Collect(AF_INET6, rowSize: 56, portOffset: 20, pidOffset: 52);
        return result;

        void Collect(int family, int rowSize, int portOffset, int pidOffset)
        {
            int size = 0;
            GetExtendedTcpTable(null, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (size <= 0) return;
            var table = new byte[size + 4096]; // запас: таблица могла вырасти между двумя вызовами
            size = table.Length;
            if (GetExtendedTcpTable(table, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return;
            int count = BitConverter.ToInt32(table, 0);
            for (int i = 0; i < count; i++)
            {
                int row = 4 + i * rowSize;
                if (row + rowSize > table.Length) break;
                // порт записан в сетевом порядке байтов в младших двух байтах
                int rowPort = (table[row + portOffset] << 8) | table[row + portOffset + 1];
                int pid = BitConverter.ToInt32(table, row + pidOffset);
                if (rowPort == port && pid > 4 && !result.Contains(pid)) result.Add(pid);
            }
        }
    }

    // ───────── дерево процессов ─────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    // процесс -> (родитель, имя файла) на данный момент
    static Dictionary<int, (int Parent, string Name)> Snapshot()
    {
        var map = new Dictionary<int, (int, string)>();
        IntPtr snapshot = CreateToolhelp32Snapshot(0x00000002 /*TH32CS_SNAPPROCESS*/, 0);
        if (snapshot == new IntPtr(-1)) return map;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32FirstW(snapshot, ref entry)) return map;
            do map[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, entry.szExeFile);
            while (Process32NextW(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return map;
    }

    /// <summary>Родители процесса, от ближайшего, не дальше <paramref name="depth"/> поколений.</summary>
    public static IReadOnlyList<int> ParentChain(int pid, int depth)
    {
        var map = Snapshot();
        var chain = new List<int>();
        int current = pid;
        for (int i = 0; i < depth && map.TryGetValue(current, out var info) && info.Parent > 4 && !chain.Contains(info.Parent); i++)
        {
            chain.Add(info.Parent);
            current = info.Parent;
        }
        return chain;
    }

    /// <summary>Все потомки процесса.</summary>
    public static IReadOnlyList<int> Descendants(int pid)
    {
        var map = Snapshot();
        var result = new List<int>();
        var queue = new Queue<int>([pid]);
        while (queue.Count > 0)
        {
            int parent = queue.Dequeue();
            foreach (var (child, info) in map)
                if (info.Parent == parent && child != pid && !result.Contains(child))
                {
                    result.Add(child);
                    queue.Enqueue(child);
                }
        }
        return result;
    }

    public static string Name(int pid) => Snapshot().TryGetValue(pid, out var info) ? info.Name : "unknown";

    public static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    public static DateTime? StartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return null; }
    }

    /// <summary>Завершает процесс со всеми потомками. Уже завершившийся процесс — не ошибка.</summary>
    public static void KillTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or AggregateException) { }
    }

    // ───────── рабочая папка чужого процесса ─────────

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr process, out bool wow64);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);

    /// <summary>
    /// Папка, из которой работает процесс, или null, если её не удалось узнать (нет доступа, 32-разрядный процесс).
    /// Читается из блока параметров процесса: другого способа Windows не даёт.
    /// </summary>
    public static string? WorkingDirectory(int pid)
    {
        if (pid <= 4 || !Environment.Is64BitProcess) return null;
        IntPtr process = OpenProcess(0x0400 /*QUERY_INFORMATION*/ | 0x0010 /*VM_READ*/, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (IsWow64Process(process, out bool wow64) && wow64) return null; // у 32-разрядных другая раскладка
            var info = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(process, 0, ref info, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0) return null;

            // PEB + 0x20 -> параметры процесса; параметры + 0x38 -> текущая папка (длина в байтах и указатель на текст)
            if (!Read(info.PebBaseAddress + 0x20, 8, out var pointer)) return null;
            IntPtr parameters = new(BitConverter.ToInt64(pointer, 0));
            if (!Read(parameters + 0x38, 16, out var unicodeString)) return null;
            int length = BitConverter.ToUInt16(unicodeString, 0);
            IntPtr text = new(BitConverter.ToInt64(unicodeString, 8));
            if (length <= 0 || length > 65534 || !Read(text, length, out var bytes)) return null;
            return Encoding.Unicode.GetString(bytes);
        }
        finally { CloseHandle(process); }

        bool Read(IntPtr address, int size, out byte[] buffer)
        {
            buffer = new byte[size];
            return ReadProcessMemory(process, address, buffer, size, out var read) && read.ToInt64() == size;
        }
    }
}
