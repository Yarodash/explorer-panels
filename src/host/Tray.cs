using System.Drawing.Drawing2D;
using ExplorerPanel.Core;

namespace ExplorerPanel.Host;

/// <summary>
/// Значок в трее. По нему видно, что программа работает: зелёная точка — панели включены, серая — выключены;
/// в подсказке — сколько панелей на экране и сколько серверов запущено. Двойной щелчок включает и выключает панели.
/// </summary>
sealed class Tray : IDisposable
{
    readonly NotifyIcon _icon;
    readonly HostContext _host;
    readonly ContextMenuStrip _menu = new();
    readonly Icon _on = DrawIcon(enabled: true), _off = DrawIcon(enabled: false);
    string _shownStatus = "";

    public Tray(HostContext host)
    {
        _host = host;
        _icon = new NotifyIcon { Visible = true, ContextMenuStrip = _menu };
        _icon.DoubleClick += (_, _) => _host.Enabled = !_host.Enabled;
        _menu.Opening += (_, _) => Rebuild(); // меню собирается заново при каждом открытии — в нём всегда текущее состояние
        _host.StateChanged += Refresh;
        Refresh();
        Rebuild();
    }

    int RunningServers => _host.Projects.All.Sum(p => p.OwnProcessCount);

    string Status => _host.Enabled ? Strings.StatusOn(_host.ShownPanels, RunningServers) : Strings.StatusOff;

    // значок и подсказка отражают текущее состояние
    void Refresh()
    {
        var status = Status;
        if (status == _shownStatus) return;
        _shownStatus = status;
        _icon.Icon = _host.Enabled ? _on : _off;
        var text = $"{AppInfo.Name} — {status}";
        _icon.Text = text.Length > 127 ? text[..127] : text; // подсказка значка не длиннее 127 знаков
    }

    void Rebuild()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new ToolStripMenuItem($"{AppInfo.Name} {AppInfo.Version}") { Enabled = false });
        _menu.Items.Add(new ToolStripMenuItem(Status) { Enabled = false });

        var toggle = new ToolStripMenuItem(_host.Enabled ? Strings.TurnOff : Strings.TurnOn);
        toggle.Font = new Font(toggle.Font, FontStyle.Bold); // главное действие; оно же — по двойному щелчку
        toggle.Click += (_, _) => _host.Enabled = !_host.Enabled;
        _menu.Items.Add(toggle);
        _menu.Items.Add(new ToolStripSeparator());

        // проекты, у которых панель запустила серверы
        var running = _host.Projects.All.Where(p => p.OwnProcessCount > 0).ToList();
        if (running.Count == 0) _menu.Items.Add(new ToolStripMenuItem(Strings.NoServers) { Enabled = false });
        foreach (var project in running)
        {
            var item = new ToolStripMenuItem(Strings.StopServers(Path.GetFileName(project.Root)));
            item.Click += (_, _) => _ = Task.Run(() => project.DoAsync("stop"));
            _menu.Items.Add(item);
        }
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(FolderList(Strings.Allowed, _host.Trust.Trusted));
        _menu.Items.Add(FolderList(Strings.Blocked, _host.Trust.Blocked));

        var log = new ToolStripMenuItem(Strings.OpenLog);
        log.Click += (_, _) => new WindowsShell().OpenInNotepad(AppPaths.HostLog);
        _menu.Items.Add(log);

        _menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(Strings.Exit);
        exit.Click += (_, _) => _host.ExitThread();
        _menu.Items.Add(exit);
    }

    // Список папок; щелчок по папке забывает решение о ней — в следующий раз панель снова спросит разрешение.
    ToolStripMenuItem FolderList(string title, IReadOnlyList<string> folders)
    {
        var list = new ToolStripMenuItem(title);
        if (folders.Count == 0) list.DropDownItems.Add(new ToolStripMenuItem(Strings.None) { Enabled = false });
        foreach (var folder in folders)
        {
            var item = new ToolStripMenuItem(Strings.Forget(folder));
            item.Click += (_, _) =>
            {
                _host.Trust.Set(folder, null);
                _host.Rescan();
            };
            list.DropDownItems.Add(item);
        }
        return list;
    }

    // Значок рисуется на лету: тёмный скруглённый прямоугольник (панель), точка статуса и треугольник «запустить».
    // Включено — зелёная точка и белый треугольник; выключено — всё серое.
    static Icon DrawIcon(bool enabled)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var panel = new GraphicsPath();
            var box = new Rectangle(1, 7, 30, 18);
            const int r = 6;
            panel.AddArc(box.Left, box.Top, r * 2, r * 2, 180, 90);
            panel.AddArc(box.Right - r * 2, box.Top, r * 2, r * 2, 270, 90);
            panel.AddArc(box.Right - r * 2, box.Bottom - r * 2, r * 2, r * 2, 0, 90);
            panel.AddArc(box.Left, box.Bottom - r * 2, r * 2, r * 2, 90, 90);
            panel.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(43, 43, 43));
            using var border = new Pen(enabled ? Color.FromArgb(170, 170, 170) : Color.FromArgb(95, 95, 95), 1.5f);
            g.FillPath(fill, panel);
            g.DrawPath(border, panel);
            using var dot = new SolidBrush(enabled ? Color.FromArgb(63, 185, 80) : Color.FromArgb(110, 110, 110));
            using var play = new SolidBrush(enabled ? Color.White : Color.FromArgb(110, 110, 110));
            g.FillEllipse(dot, 6, 12.5f, 7, 7);
            g.FillPolygon(play, new[] { new PointF(18, 11), new PointF(18, 21), new PointF(26, 16) });
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _host.StateChanged -= Refresh;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _on.Dispose();
        _off.Dispose();
    }
}
