# How it works

Windows 11 File Explorer has no supported way to add a button to its window. Explorer Panels does not modify
Explorer or inject code into it. Instead it keeps a separate, transparent window glued to each Explorer window
so that it behaves like a part of it.

## Parts

| Part | Where | Role |
|---|---|---|
| Core | `src/core` | The `panel.json` format: loading, validation with line numbers, placeholders, trust store, status rules, the client of the local interface. No windows, no processes — fully unit-tested. |
| Host | `src/host` → `explorer-panel-host.exe` | The background program with the tray icon: follows Explorer windows, shows panel windows, runs services, actions and value handlers, serves the local interface. |
| CLI | `src/cli` → `explorer-panel.exe` | The command used by agents and scripts. Talks to the host over the local interface; `check` and `trust` also work without it. |
| Page | `src/ui` | The panel itself: React + shadcn/ui, built once and loaded by the host into WebView2. It renders whatever state the host sends. |

## Following Explorer

- Open windows and the folder of each window's **active tab** come from Explorer itself (the `Shell.Application`
  COM object). All tabs of a window share one window handle; the active tab is the one whose tab window is the
  topmost `ShellTabWindowClass` child.
- WinEvent hooks report moves, resizes, minimising, tab switches and virtual-desktop changes. A one-second
  rescan is the safety net; it also notices a `panel.json` that has just appeared.
- For the folder shown, the project is the nearest ancestor containing `.explorer_components/panel.json`.

## The panel window

- It is a top-level window whose **owner** is the Explorer window. Windows then minimises it with Explorer and
  moves it to another virtual desktop together with it.
- A newly shown window is placed at the top of the window stack, so it is explicitly re-inserted right above
  its owner. Otherwise it would float over other programs while Explorer is behind them.
- Styles: no activation (`WS_EX_NOACTIVATE`), tool window, no `WS_EX_APPWINDOW` — so it is not listed in the
  taskbar or Alt+Tab.
- **Focus.** A press on the panel moves keyboard focus into the browser engine. The page tells the host when
  the press is over and no menu, list or calendar is open; the host then gives focus back to Explorer, and only
  if the panel itself is the foreground window. It must not be returned earlier: Radix menus close when the
  page loses focus. Another program's active window is never touched.
- It is anchored to the file list area (`SHELLDLL_DefView`), not to the window frame, so it never covers the
  navigation pane, the status bar or the details pane.
- **Transparency.** The window has no surface of its own (`WS_EX_NOREDIRECTIONBITMAP`); only the page, drawn by
  WebView2 with a transparent background, is on screen. Shadows and rounded corners are real per-pixel alpha.
- **Click-through.** The window is a canvas larger than the panel, leaving room for tooltips, the calendar and
  menus. Every frame the page reports the rectangles of its visible parts and the window is clipped to them
  (`SetWindowRgn`); everything else does not exist for the mouse, so clicks reach Explorer.
- When the panel does not fit, the page hides whole widgets and offers a button; the host then widens the
  Explorer window by exactly the missing width, staying inside the monitor's work area.

## Services

- A service command runs hidden through `cmd.exe`; output is read line by line (UTF-8, falling back to the
  console code page for `cmd`'s own messages) into a rotating log.
- The output is read by a small go-between process (the host executable started with `--pump`), not by the
  host itself. If the host read it, closing the host would break the service's output and the service with it;
  the go-between lives exactly as long as the service.
- Start and stop of one service never overlap.
- The status is computed by one pure function (`ServiceStatusRules`) from observations: does `health` answer,
  is the process the panel started alive, how did it exit, who listens on the port.
- **Ownership.** A listening process belongs to the project if it descends from a process the panel started,
  or if its working directory is inside the project folder (read from the process's parameter block).
  Anything else on the port is foreign: it is reported as a failure and never ended.
- The panel-started process is remembered by PID and start time, so services survive a host restart.
- `localhost` is probed on both `127.0.0.1` and `::1` at once: on Windows a connection attempt to the family
  the server does not listen on stalls for about two seconds.

## Trust

A folder's commands (services, actions, handlers) run only after the folder is allowed, and until then none of
its `health` addresses is contacted. The decision is stored per project root in
`%LOCALAPPDATA%\ExplorerPanels\trust.json`, shared by the host and the CLI. Until then the page shows a question
instead of the panel. The file lives in the user's profile, so allowing a folder takes the user's own account:
there is no request that does it.

## Local interface

`http://127.0.0.1:47631/v1/…` (the next free port if that one is taken). The actual port and a random access
key, new on every start, are written to `%LOCALAPPDATA%\ExplorerPanels\host.json`.

A request is accepted only if all of this holds:

- it carries the key in the header `X-Explorer-Panel` — the file is in the user's profile, so other users of
  the computer cannot read it;
- it has no `Origin` header — browsers add one to requests made by pages;
- its `Host` is `127.0.0.1:<port>` or `localhost:<port>` — a page whose site name was pointed at 127.0.0.1
  would address the request to that name.

| Request | Body / query | Purpose |
|---|---|---|
| `GET /v1/ping` | | version and PID |
| `GET /v1/status` | `?folder=` | project root, trust, problems, services, values |
| `POST /v1/set` | `{ folder, values }` | set values instantly |
| `POST /v1/do` | `{ folder, action }` | run an action and return its result (allowed folders only) |
| `POST /v1/reload` | `{ folder }` | re-read `panel.json` |

## Cost

The host uses about 80 MB. The browser engine adds a few hundred megabytes while panels are visible and is
released after five minutes without a visible panel.

## Testing

Window tests never touch the user's desktop: they run on a separate virtual desktop with their own Explorer
window, never activate windows, never simulate keys, and capture individual windows with
Windows.Graphics.Capture (`tools/winshot`) instead of the screen. See `tests/e2e`.
