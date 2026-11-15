---
name: explorer-panel
description: Add or change the control panel that Explorer Panels shows inside Windows File Explorer for the current project — status dots for its servers and buttons to start them, open the site, stop them, run tests, read logs. Use when the user says «добавь управление в проводник», «добавь кнопок управления текущим проектом», «добавь панель в проводник», "add explorer panel", "add control buttons to explorer", or asks for buttons or statuses for this project in File Explorer.
---

# Explorer Panels: add a panel to the current project

Explorer Panels is a background program on this machine. For any folder that contains
`.explorer_components/panel.json` it shows a small panel in the bottom-right corner of File Explorer.
Your job is to write that one file for the current project. No build step, no dependencies.

## Steps

1. **Check the tool:** `explorer-panel version`. If the command is missing, tell the user Explorer Panels is not
   installed (repository: https://github.com/Yarodash/explorer-panels, install with `scripts\install.ps1`) and stop.
2. **Learn how the project runs.** Read `package.json` scripts, `Makefile`, `docker-compose.yml`, `pyproject.toml`,
   the README, existing run scripts. You need: the exact start command of each server, the folder it runs in,
   the address it answers on, and the handful of commands the user runs most (tests, build, migrations).
   If a server's port or command cannot be determined from the project, ask the user rather than guess.
3. **Write `.explorer_components/panel.json`** in the project root (format below). If the file already exists,
   edit it instead of replacing it.
4. **Allow and check:**
   ```
   explorer-panel trust .
   explorer-panel check .
   ```
   `check` must end with `OK` and exit code 0. Each problem is a line
   `panel.json:line:column: error: message` with a suggested fix. Fix and repeat until clean; warnings usually
   mean a typo.
5. **Look at it** (optional, when the folder is open in File Explorer): `explorer-panel screenshot` saves a
   PNG of the panel and prints its path — read the image to see what the user sees.
6. **Tell the user** what the panel contains and that it is visible in File Explorer in the project folder
   (and its subfolders). Do not start the servers yourself unless asked.

## Format

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
    { "type": "separator" },
    { "type": "button", "icon": "play", "tooltip": "Запустить", "do": "start", "pin": true },
    { "type": "button", "icon": "globe", "tooltip": "Открыть сайт", "do": "site", "pin": true },
    { "type": "button", "icon": "square", "tooltip": "Выключить", "do": "stop", "pin": true },
    { "type": "button", "icon": "flask-conical", "tooltip": "Тесты", "do": "tests" },
    { "type": "button", "icon": "file-text", "tooltip": "Логи", "do": "logs" }
  ]
}
```

- **`services`** — long-running processes. `run` (required) starts it. `health` is a URL that answers when it
  is up, or `{ "run": "command" }` that exits with 0. `stop` is an optional stop command (give it together with
  `health` for launchers such as `docker compose up -d` / `docker compose down`). `cwd`, `env` optional.
- **`actions`** — exactly one of `run` (a command) or `open` (an address, file or folder). `needs` lists
  services to start and wait for first. Built-in, never declared: `start`, `stop`, `restart`, `start:name`,
  `stop:name`, `restart:name`, `logs`.
- **`values`** — `{ "run": "...", "every": 20 }` runs a command every N seconds and shows its output;
  `{ "default": "dev" }` is a stored value changed by a toggle or select; `{}` is a value pushed from outside
  with `explorer-panel set name=value`. A handler may print a JSON envelope
  `{ "value": ..., "ttl": seconds, "widgets": [...] }`; a `slot` widget shows those widgets.
- **`widgets`** — left to right. Omit the key entirely to get a sensible default panel (statuses, Start, Stop,
  a button per action, Logs): for a simple project `services` alone is a complete file.

| `type` | Keys |
|---|---|
| `status` | `service` or `value`; `label` |
| `button` | `icon` and/or `label`; `do` |
| `text` | `text`; `icon` |
| `toggle` | `value`; `label`, `on`, `off` (actions) |
| `select` | `value`, `options`; `do` |
| `progress` | `value` (0–100); `label` |
| `menu` | `icon` and/or `label`; `items`: `[{ "label", "do" }]` |
| `clock` | `seconds` |
| `stopwatch` | — |
| `calendar` | `do` (the action gets `{date}` as `YYYY-MM-DD`) |
| `separator` | — |
| `slot` | `value` |

Every widget may have `tooltip` and `pin` (`true` keeps it when the panel is collapsed or too wide).

## Rules

- Commands run through `cmd.exe` from the **project root**; use `cwd` for a subfolder. Write them exactly as
  they would be typed in a Command Prompt (`npm run dev`, not a PowerShell-only form).
- `{name}` inserts a value into `text`, `tooltip`, `open` and `run`. In `run` only values made of letters,
  digits and `. _ - / : @ +` are substituted. A literal brace is doubled: `git rev-list @{{u}}..HEAD`.
- A command is a single line; join several with `&&`.
- Icons are lucide names in kebab-case (`play`, `square`, `globe`, `file-text`, `flask-conical`, `hammer`,
  `refresh-cw`, `terminal`, `database`, `git-branch`, `ellipsis`). `check` rejects unknown names and suggests
  the nearest.
- Tooltips and labels in the user's language.
- Keep the panel short: statuses, then the three or four buttons used daily. Put the rest in a `menu`.
  Mark the essential buttons with `"pin": true`.
- A port must match what the server really listens on; a wrong `health` shows the service as never starting.
- No secrets in `panel.json`; it is meant to be committed. The program adds `.explorer_components/.gitignore`
  for its logs.
- To show a status that only the project's own program knows, declare a value and bind a status to it:
  `{ "type": "status", "value": "deploy" }`, then the program runs `explorer-panel set deploy=running`
  (`stopped`, `starting`, `running`, `failed`).

## Other commands

`explorer-panel status` (services and values as JSON), `start | stop | restart [service]`, `do <action>`,
`set name=value`, `logs`, `screenshot [--out file]`. Full reference: `docs/panel-reference.md` in the repository.
