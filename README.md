# Explorer Panels

A control panel for your project, inside the real Windows 11 File Explorer.

Open a project folder and a small panel sits in the bottom-right corner of the file list: status dots for
your servers, buttons to start them, open the site, stop them, read the logs — or anything else you describe.
The panel belongs to the folder: it is one file, `.explorer_components/panel.json`.

## Add a panel to a project

This section is all an AI agent (or a person) needs.

1. Create `.explorer_components/panel.json` in the project root:

```json
{
  "services": {
    "web": { "run": "npm run dev", "health": "http://localhost:5173" }
  }
}
```

2. In the project root run:

```
explorer-panel trust .
explorer-panel check .
```

`check` must end with `OK`. If it prints problems, each one is a line `panel.json:line:column: error: message`
with a suggested fix — correct the file and run `check` again. The panel appears in File Explorer at once and
reloads whenever the file changes.

That minimal file already gives a status dot for the service plus Start, Stop and Logs buttons.
To choose the buttons yourself, add `actions` and `widgets`:

```json
{
  "services": {
    "api": { "run": "python -m uvicorn main:app --port 8000", "cwd": "backend", "health": "http://localhost:8000/docs" },
    "web": { "run": "npm run dev", "cwd": "frontend", "health": "http://localhost:5173" }
  },
  "actions": {
    "site": { "open": "http://localhost:5173", "needs": ["api", "web"] },
    "tests": { "run": "pytest -q", "cwd": "backend" }
  },
  "values": {
    "branch": { "run": "git branch --show-current", "every": 20 }
  },
  "widgets": [
    { "type": "text", "icon": "git-branch", "text": "{branch}" },
    { "type": "status", "service": "api" },
    { "type": "status", "service": "web" },
    { "type": "button", "icon": "play", "tooltip": "Start", "do": "start", "pin": true },
    { "type": "button", "icon": "globe", "tooltip": "Open the site", "do": "site", "pin": true },
    { "type": "button", "icon": "square", "tooltip": "Stop", "do": "stop", "pin": true },
    { "type": "button", "icon": "flask-conical", "tooltip": "Run the tests", "do": "tests" },
    { "type": "button", "icon": "file-text", "tooltip": "Logs", "do": "logs" }
  ]
}
```

How to fill it in:

- **Find out how the project is started** — `package.json` scripts, `Makefile`, `docker-compose.yml`, the README —
  and put those exact commands in `run`. Commands run through `cmd.exe` from the project root; `cwd` picks a subfolder.
- **`health`** is a URL that answers when the service is up. The status dot is gray (stopped), blinking amber
  (starting), green (running) or red (failed).
- **`needs`** lists services an action requires; they are started and awaited first.
- **Icons** are [lucide](https://lucide.dev/icons) names (`play`, `globe`, `git-branch`).
- **Tooltips** go in the user's language.
- Write only the fields from [docs/panel-reference.md](docs/panel-reference.md). Complete examples for common
  kinds of projects are in [examples/](examples/).

## Install

Requires Windows 11, [.NET SDK 10](https://dotnet.microsoft.com/download) and [Node.js 20+](https://nodejs.org).

```
git clone https://github.com/Yarodash/explorer-panels
cd explorer-panels
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

The script builds the program, copies it to `%LOCALAPPDATA%\Programs\ExplorerPanels`, makes it start at sign-in,
puts `explorer-panel` on your PATH and starts it (a tray icon appears). Run it again to update.
`scripts\uninstall.ps1` removes it.

## The `explorer-panel` command

| Command | What it does |
|---|---|
| `check` | Validate `panel.json`; one line per problem; exit code 1 on errors. Also says whether the panel is loaded and allowed. |
| `trust`, `untrust` | Allow this folder's panel to run commands; forget the decision. |
| `status` | Services and values of the project as JSON. |
| `start`, `stop`, `restart [service]` | Control all services or one. |
| `do <action>` | Run an action from `panel.json`. |
| `set name=value ...` | Set values instantly (see below). |
| `logs` | Print the log file path and its last lines. |

The folder defaults to the current one; the project is found by walking up from it.

## Updating the panel from your own program

Any program can change what the panel shows, instantly:

```
explorer-panel set deploy=running
```

or with a request to the local interface. Its port and access key are in
`%LOCALAPPDATA%\ExplorerPanels\host.json`, readable only by you:

```python
import json, os, urllib.request
host = json.load(open(os.path.expandvars(r"%LOCALAPPDATA%\ExplorerPanels\host.json")))
urllib.request.urlopen(urllib.request.Request(
    f"http://127.0.0.1:{host['port']}/v1/set",
    data=json.dumps({"folder": r"D:\my-project", "values": {"deploy": "running"}}).encode(),
    headers={"Content-Type": "application/json", "X-Explorer-Panel": host["token"]}))
```

A `status` widget can follow such a value: `{ "type": "status", "value": "deploy" }`.

## Claude buttons

Every folder — with or without a panel — gets two buttons with the Claude icon at the right end:

- the orange one opens [Claude Code](https://claude.com/claude-code) in a terminal in the folder shown;
- the red one continues the last conversation there (`claude --continue`).

They need `claude` on your PATH. Turn them off and on with **Claude buttons in every folder** in the tray
icon's menu; turning panels off hides them too.

## Safety

- A panel can run commands, so a folder must be allowed first: one click on the question the panel shows, or
  `explorer-panel trust`. Until then nothing from the folder runs and no address from it is contacted.
- The local interface listens on this computer only and needs a random key that is stored in your user
  profile; requests from web pages are refused. A folder cannot be allowed through it.
- Stop only ever stops processes of the project: those the panel started, or those running from inside the
  project folder. Another program on the same port is reported as a problem and left alone.
- A value is substituted into a command only if it consists of letters, digits and `. _ - / : @ +`.

## More

- [docs/panel-reference.md](docs/panel-reference.md) — every field of `panel.json`.
- [docs/how-it-works.md](docs/how-it-works.md) — how the panel is attached to File Explorer.
- [examples/](examples/) — ready-made panels.
- [skill/explorer-panel/](skill/explorer-panel/) — a Claude Code skill; copy it to `~/.claude/skills/` and say
  "add explorer panel" (or «добавь управление в проводник») in any project.

## Development

```
dotnet test ExplorerPanels.slnx          # core, host and CLI tests (need Python for the service tests)
npm --prefix src/ui test                 # panel page tests
powershell -File tests\e2e\run.ps1       # end-to-end, on a separate virtual desktop
```

Layout: `src/core` (format, validation, rules), `src/host` (background program), `src/cli` (the command),
`src/ui` (panel page: React + shadcn/ui), `tools/winshot` (window capture for tests).

The Claude icon comes from [Simple Icons](https://simpleicons.org) (CC0). Claude is a trademark of Anthropic;
this project is not affiliated with Anthropic.
