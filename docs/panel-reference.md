# `panel.json` reference

A project is a folder that contains `.explorer_components/panel.json`. The panel is shown in File Explorer in
that folder and, by default, in all of its subfolders. The nearest `.explorer_components` up the tree wins.

```
my-project/
  .explorer_components/
    panel.json       required
    handlers/        optional: scripts used by "values"
    logs/            written by the program
    .gitignore       written by the program if missing (ignores logs/)
```

The file is JSON; comments and trailing commas are accepted. After every change run `explorer-panel check`.

## Top level

| Key | Meaning |
|---|---|
| `version` | Format version. Only `1` exists. Optional. |
| `scope` | `"tree"` (default): the panel shows in the project root and every subfolder. `"folder"`: only in the root. |
| `services` | Long-running processes: servers, watchers. |
| `actions` | One-shot things: run a command, open an address. |
| `values` | Named pieces of data shown by widgets and substituted into texts and commands. |
| `widgets` | What the panel shows, left to right. Optional. |

If `widgets` is absent the panel is built automatically: a status per service, Start and Stop, a button per
action, Logs. So the smallest useful file is just `services`.

## How commands run

Everywhere a command is given the key is `run`.

- Commands run through `cmd.exe /c`, exactly as if typed into a Command Prompt: pipes, `&&`, quotes and
  `.cmd` shims such as `npm` work. A command is one line; join several with `&&`.
- The working folder is the **project root** (the folder that contains `.explorer_components`) — for services,
  actions and value handlers alike. `cwd` selects another folder, relative to the project root.
- `env` adds environment variables: `{ "env": { "DEBUG": "1" } }`.
- Nothing is shown on screen. Output goes to `.explorer_components/logs/services.log` line by line, as it is
  produced.
- Commands get the environment of a normal sign-in, not the environment of whatever started the background
  program.
- Commands run only after the folder has been allowed (`explorer-panel trust`).

## `services`

```json
{
  "services": {
    "api": {
      "run": "python -m uvicorn main:app --port 8000",
      "cwd": "backend",
      "env": { "DEBUG": "1" },
      "health": "http://localhost:8000/docs"
    },
    "db": {
      "run": "docker compose up -d",
      "stop": "docker compose down",
      "health": "http://localhost:8080"
    }
  }
}
```

| Key | Meaning |
|---|---|
| `run` | Required. The command that starts the service. |
| `cwd`, `env` | Optional, see above. |
| `health` | How to tell the service is up. A URL — up when it answers with a status below 500 — or `{ "run": "command" }` — up when the command exits with code 0. Without `health` the service counts as running while its process is alive. |
| `stop` | Optional command that stops the service. Without it the process tree is ended. |

Statuses:

| Status | Colour | When |
|---|---|---|
| `stopped` | gray | not started, or stopped by the user |
| `starting` | amber, blinking | the process is running but `health` does not answer yet |
| `running` | green | `health` answers (or, without `health`, the process is alive) |
| `failed` | red | the process exited by itself, could not be started, or the port is taken by another program |

Details worth knowing:

- A service started outside the panel (from a terminal) is still shown as `running` if it runs from inside the
  project folder, and Stop stops it.
- If the port in `health` is held by a program that does not belong to the project, the status is `failed`
  with the program's name. The panel never ends such a process.
- A start command that exits with code 0 (a launcher like `docker compose up -d`) is not a failure; the status
  then follows `health` only, so give such services both `health` and `stop`.
- Services keep running when the background program exits or is updated; when it starts again it finds them.
- A running service that is removed from `panel.json` (or renamed) is stopped: the panel started it, and after
  the change nothing else could stop it.
- A port held by Docker's or WSL's port forwarder does not count as another program.
- Until the folder is allowed, `health` addresses are not contacted.

## `actions`

```json
{
  "services": { "web": { "run": "npm run dev", "health": "http://localhost:5173" } },
  "actions": {
    "site": { "open": "http://localhost:5173", "needs": ["web"] },
    "tests": { "run": "npm test" },
    "folder": { "open": "dist" }
  }
}
```

An action has exactly one of:

| Key | Meaning |
|---|---|
| `run` | A command. The button shows a spinner while it runs, then a tick or a cross. |
| `open` | An address, file or folder to open with Windows. Relative paths are relative to the project root. |

Optional: `cwd`, `env`, and `needs` — services that must be running first; they are started and awaited
(up to 90 seconds).

Built-in actions, available in every panel and not declared in `actions`:

| Action | What it does |
|---|---|
| `start` | start all services |
| `stop` | stop all services |
| `restart` | stop, then start, all services |
| `start:name`, `stop:name`, `restart:name` | the same for one service |
| `logs` | open the log file in Notepad |

## `values`

A value is a named piece of data. It has one of three sources.

```json
{
  "values": {
    "branch": { "run": "git branch --show-current", "every": 20 },
    "mode": { "default": "dev" },
    "deploy": {}
  }
}
```

| Source | Declared as | Behaviour |
|---|---|---|
| Handler | `{ "run": "command", "every": 20 }` | The command runs every `every` seconds (default 30) while the panel is visible; its output becomes the value. |
| Stored | `{ "default": "dev" }` or `{}` | Kept by the program; changed by a `toggle` or `select` widget; survives restarts. |
| Pushed | `{}` | Set from outside at any moment: `explorer-panel set deploy=running`. Declaring it is optional but avoids a warning. |

Handler output:

- plain text — the value;
- JSON — the value, parsed;
- a JSON object with any of the keys `value`, `ttl`, `widgets` — an envelope: `value` is the value, `ttl` is
  the number of seconds until the next run, `widgets` is a list of widgets shown by a `slot` widget.

```json
{ "value": "3 new", "ttl": 120, "widgets": [ { "type": "text", "icon": "git-commit-horizontal", "text": "3 new commits" } ] }
```

## Placeholders

`{name}` is replaced by the current value `name`:

- in `text`, `tooltip` and `label` — as is; an unknown name becomes empty;
- in `open` — when the target is an address (`http://…`, `vscode://…`), the value is encoded as part of the
  address. When it is a file or folder, the value may choose something **inside the project folder** only;
  a value that leads elsewhere (`..`, an absolute path, an address) makes the action fail;
- in `run` — only if the value consists of letters, digits and `. _ - / : @ +`, and does not start an argument
  with `-` or `/` (the command would read it as an option; `--mode={mode}` is fine). Otherwise the command is
  not run and the reason is written to the log. This keeps a branch name or pushed text from becoming a command.

To write a literal brace, double it: `"run": "git rev-list @{{u}}..HEAD --count"` runs
`git rev-list @{u}..HEAD --count`.

An action started by a `calendar` widget also gets `{date}` (`YYYY-MM-DD`).

## `widgets`

Each widget is an object with a `type`. Common optional keys: `tooltip` (text shown on hover) and `pin`
(`true` keeps the widget when the panel is collapsed or too wide; statuses are always kept).

| `type` | Shows | Keys |
|---|---|---|
| `status` | a status dot with a label | `service` **or** `value`; optional `label` |
| `button` | an icon button | `icon` and/or `label`; `do` (action) |
| `text` | text, usually with a value | `text`; optional `icon` |
| `toggle` | an on/off switch | `value`; optional `label`, `on`, `off` (actions run when switched) |
| `select` | a choice between options | `value`, `options` (list of strings); optional `do` (action run after a change) |
| `progress` | a bar from 0 to 100 | `value`; optional `label` |
| `menu` | a button with a drop-down list | `icon` and/or `label`; `items`: list of `{ "label", "do" }` |
| `clock` | the current time | optional `seconds` (`false` hides seconds) |
| `stopwatch` | a stopwatch | — |
| `calendar` | a date button with a pop-up calendar | `do` (action that receives `{date}`); optional `icon` |
| `separator` | a thin divider | — |
| `slot` | widgets returned by a handler | `value` |

```json
{
  "services": { "api": { "run": "python app.py", "health": "http://localhost:8000" } },
  "actions": {
    "site": { "open": "http://localhost:8000" },
    "report": { "open": "http://localhost:8000/report?day={date}" },
    "build": { "run": "npm run build -- --mode {mode}" }
  },
  "values": { "mode": { "default": "dev" }, "watch": { "default": false }, "coverage": {}, "deploy": {} },
  "widgets": [
    { "type": "clock", "seconds": false },
    { "type": "stopwatch" },
    { "type": "separator" },
    { "type": "toggle", "value": "watch", "label": "watch" },
    { "type": "select", "value": "mode", "options": ["dev", "prod"], "do": "build" },
    { "type": "progress", "value": "coverage", "label": "coverage" },
    { "type": "calendar", "do": "report", "tooltip": "Open the report for a day" },
    { "type": "separator" },
    { "type": "status", "service": "api" },
    { "type": "status", "value": "deploy", "label": "deploy" },
    { "type": "button", "icon": "play", "tooltip": "Start", "do": "start", "pin": true },
    { "type": "button", "icon": "globe", "tooltip": "Open", "do": "site", "pin": true },
    { "type": "button", "icon": "square", "tooltip": "Stop", "do": "stop", "pin": true },
    { "type": "menu", "icon": "ellipsis", "tooltip": "More", "items": [
      { "label": "Restart the API", "do": "restart:api" },
      { "label": "Logs", "do": "logs" }
    ] }
  ]
}
```

A `status` bound to a `value` expects `"stopped"`, `"starting"`, `"running"` or `"failed"`, or an object
`{ "state": "failed", "label": "db", "detail": "disk full" }`.

Icons are [lucide](https://lucide.dev/icons) names in kebab-case. `check` rejects an unknown name and suggests
the nearest one.

When the panel does not fit into the file list area, the leftmost widgets are hidden whole and a button
appears that widens the Explorer window by exactly the missing amount.

## Checking your work

```
explorer-panel check
```

```
panel.json:12:27: error: unknown icon "playy" (did you mean "play"?); use a lucide icon name such as "play"
panel.json:4:7: warning: unknown key "helth" (did you mean "health"?)
FAILED: 1 error, 1 warning
host: running
trusted: yes
loaded: no (the panel shows the last valid version, if there was one)
```

Errors must be fixed. Warnings point at probable typos. While the file has errors, the panel keeps showing
its last valid version with a warning sign. If the page could not draw the panel, `check` prints the reason as
`last panel error`.

To see the result:

```
explorer-panel screenshot
```

saves a PNG of the panel and prints its path (`.explorer_components/logs/panel.png`, or `--out file.png`).
The picture is taken by the panel page itself, so the folder must be open in File Explorer, but the window
need not be in front.

An optional `"$schema"` key may point at [schema/panel.schema.json](../schema/panel.schema.json) for editor
completion.
