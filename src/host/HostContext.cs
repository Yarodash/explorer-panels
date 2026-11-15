using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExplorerPanel.Core;

namespace ExplorerPanel.Host;

/// <summary>
/// Главный объект фоновой программы: связывает слежение за Проводником с окнами панелей.
/// </summary>
sealed class HostContext : ApplicationContext
{
    readonly ExplorerTracker _tracker = new();
    readonly Dictionary<IntPtr, PanelWindow> _windows = [];
    readonly string _uiFolder;
    readonly string _locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

    public HostContext()
    {
        _uiFolder = Environment.GetEnvironmentVariable("EXPLORER_PANEL_UI") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(AppContext.BaseDirectory, "ui");
        Log.Write($"started {AppInfo.Version}, ui={_uiFolder}");

        _tracker.Changed += OnExplorerChanged;
        _tracker.Moved += explorer => { if (_windows.TryGetValue(explorer, out var window)) window.Reposition(); };
        _tracker.Start();
    }

    // Для сквозных проверок: EXPLORER_PANEL_ONLY=<номер окна> ограничивает программу одним окном Проводника,
    // чтобы проверочный экземпляр не трогал окна пользователя.
    readonly long _onlyWindow = long.TryParse(Environment.GetEnvironmentVariable("EXPLORER_PANEL_ONLY"), out long only) ? only : 0;

    void OnExplorerChanged(IReadOnlyList<ExplorerView> views)
    {
        if (_onlyWindow != 0) views = views.Where(v => v.Window.ToInt64() == _onlyWindow).ToList();

        // окна панелей, чей Проводник закрылся
        foreach (var explorer in _windows.Keys.Except(views.Select(v => v.Window)).ToList())
        {
            _windows[explorer].Dispose();
            _windows.Remove(explorer);
        }

        foreach (var view in views)
        {
            var root = ProjectLocator.FindRoot(view.Folder);
            var loaded = root != null ? PanelLoader.LoadFile(root) : null;
            bool show = loaded is { Ok: true } && !view.Minimized && ProjectLocator.Applies(root!, view.Folder!, loaded.Config!.Scope);

            if (!_windows.TryGetValue(view.Window, out var window))
            {
                if (!show) continue;
                window = new PanelWindow(view.Window, _uiFolder);
                _windows[view.Window] = window;
            }
            if (show) window.Post(StateMessage(root!, loaded!.Config!));
            window.SetVisible(show);
            if (show) window.Reposition();
        }
    }

    // Временное состояние для задачи 6: виджеты из файла, все серверы «не запущены».
    string StateMessage(string root, PanelConfig config)
    {
        var services = new JsonObject();
        foreach (var service in config.Services) services[service.Name] = new JsonObject { ["status"] = "stopped" };
        var state = new JsonObject
        {
            ["root"] = root, ["locale"] = _locale, ["trusted"] = true, ["collapsed"] = false,
            ["problems"] = new JsonArray(), ["widgets"] = config.Widgets.DeepClone(), ["services"] = services,
            ["values"] = new JsonObject(), ["actions"] = new JsonObject(), ["slots"] = new JsonObject(),
        };
        return new JsonObject { ["type"] = "state", ["state"] = state }.ToJsonString(JsonSerializerOptions.Web);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tracker.Dispose();
            foreach (var window in _windows.Values) window.Dispose();
            _windows.Clear();
        }
        base.Dispose(disposing);
    }
}
