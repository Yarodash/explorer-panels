# Examples

Each folder is a project with a ready `.explorer_components/panel.json`. Copy the file into your project,
adjust the commands and ports, then run `explorer-panel trust .` and `explorer-panel check .`.

| Example | Shows |
|---|---|
| [fastapi-react](fastapi-react/) | Two services in subfolders (`cwd`), an action that needs both, a menu. A runnable demo: a Python API and a React site. |
| [node](node/) | One dev server from `package.json` scripts, test and build buttons. |
| [docker-compose](docker-compose/) | A launcher command with its own `stop` (`docker compose up -d` / `down`). |
| [static-site](static-site/) | The smallest useful file: no `widgets`, the panel is built automatically. `scope: "folder"`. |
| [git-info](git-info/) | No services at all: values from commands and a handler script that returns widgets and its own refresh interval. |

The repository's own panel is in [../.explorer_components/panel.json](../.explorer_components/panel.json).
