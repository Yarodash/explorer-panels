using System.Drawing.Drawing2D;
using ExplorerPanel.Core;

namespace ExplorerPanel.Host;

/// <summary>Значок в трее: работающие серверы, разрешённые папки, пауза, журнал, выход.</summary>
sealed class Tray : IDisposable
{
    readonly NotifyIcon _icon;
    readonly HostContext _host;
    readonly ContextMenuStrip _menu = new();

    public Tray(HostContext host)
    {
        _host = host;
        _icon = new NotifyIcon { Icon = DrawIcon(), Text = $"{AppInfo.Name} {AppInfo.Version}", Visible = true, ContextMenuStrip = _menu };
        _menu.Opening += (_, _) => Rebuild(); // меню собирается заново при каждом открытии — в нём всегда текущее состояние
        Rebuild();
    }

    void Rebuild()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new ToolStripMenuItem($"{AppInfo.Name} {AppInfo.Version}") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        // проекты, у которых панель запустила серверы
        var running = _host.Projects.All.Where(p => p.HasOwnProcesses).ToList();
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

        var pause = new ToolStripMenuItem(Strings.Pause) { Checked = _host.Paused };
        pause.Click += (_, _) => _host.Paused = !_host.Paused;
        _menu.Items.Add(pause);

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

    // Значок рисуется на лету: тёмный скруглённый квадрат (панель), зелёная точка статуса и белый треугольник «запустить».
    static Icon DrawIcon()
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
            using var border = new Pen(Color.FromArgb(150, 150, 150), 1.5f);
            g.FillPath(fill, panel);
            g.DrawPath(border, panel);
            using var green = new SolidBrush(Color.FromArgb(63, 185, 80));
            g.FillEllipse(green, 6, 12.5f, 7, 7);
            g.FillPolygon(Brushes.White, new[] { new PointF(18, 11), new PointF(18, 21), new PointF(26, 16) });
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
