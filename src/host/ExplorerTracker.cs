namespace ExplorerPanel.Host;

/// <summary>
/// Следит за окнами Проводника: сообщает, какие окна открыты и какие папки в них показаны,
/// и отдельно — о каждом перемещении и изменении размера окна.
/// </summary>
sealed class ExplorerTracker : IDisposable
{
    readonly List<IntPtr> _hooks = [];
    readonly Native.WinEventProc _callback; // ссылка хранится, чтобы делегат не собрал сборщик мусора
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 30 };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    HashSet<IntPtr> _known = [];
    bool _scanning, _probeFailed;

    /// <summary>Свежий список окон Проводника (после каждого обхода).</summary>
    public event Action<IReadOnlyList<ExplorerView>>? Changed;

    /// <summary>Окно Проводника сдвинулось или изменило размер.</summary>
    public event Action<IntPtr>? Moved;

    public ExplorerTracker()
    {
        _callback = OnWinEvent;
        _debounce.Tick += (_, _) => { _debounce.Stop(); Rescan(); };
        // Обход раз в секунду — страховка от потерянных событий; он же замечает появление panel.json в открытой папке.
        _poll.Tick += (_, _) => Rescan();
    }

    public void Start()
    {
        Hook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND);
        Hook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND);
        Hook(Native.EVENT_OBJECT_CREATE, Native.EVENT_OBJECT_HIDE);                  // создано, уничтожено, показано, скрыто
        Hook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_NAMECHANGE);    // сдвинулось; сменился заголовок
        Hook(Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED);            // ушло на другой рабочий стол и вернулось
        _poll.Start();
        Rescan();
    }

    void Hook(uint first, uint last)
    {
        var hook = Native.SetWinEventHook(first, last, IntPtr.Zero, _callback, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return; // интересуют только сами окна

        if (evt == Native.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (_known.Contains(hwnd)) Moved?.Invoke(hwnd);
            return;
        }
        if (_known.Contains(hwnd) || Native.ClassName(hwnd) is "CabinetWClass" or "ShellTabWindowClass")
        {
            _debounce.Stop();
            _debounce.Start();
        }
    }

    public void Rescan()
    {
        if (_scanning) return; // обращение к Проводнику прокачивает сообщения — возможен повторный вход
        _scanning = true;
        try
        {
            List<ExplorerView> views;
            try
            {
                views = ExplorerProbe.Views();
                if (_probeFailed) Log.Write("explorer is reachable again");
                _probeFailed = false;
            }
            catch (Exception ex)
            {
                // Проводник перезапускается или ещё не запущен: панели прячем, попробуем при следующем обходе
                if (!_probeFailed) Log.Write("explorer is not reachable: " + ex.Message);
                _probeFailed = true;
                views = [];
            }
            _known = views.Select(v => v.Window).ToHashSet();
            Changed?.Invoke(views);
        }
        finally { _scanning = false; }
    }

    public void Dispose()
    {
        _poll.Dispose();
        _debounce.Dispose();
        foreach (var hook in _hooks) Native.UnhookWinEvent(hook);
        _hooks.Clear();
    }
}
