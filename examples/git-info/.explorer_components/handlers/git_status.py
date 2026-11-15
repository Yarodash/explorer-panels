"""Value handler: how many files are changed and how far the branch is from its upstream.

Prints an envelope: the value itself, how soon to run again ("ttl", seconds) and widgets for the "slot".
Run from the project root by Explorer Panels; see docs/panel-reference.md ("values").
"""
import json
import subprocess


def git(*args: str) -> str:
    result = subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8")
    return result.stdout.strip() if result.returncode == 0 else ""


changed = len([line for line in git("status", "--porcelain").splitlines() if line])
ahead, behind = 0, 0
counts = git("rev-list", "--left-right", "--count", "@{upstream}...HEAD").split()
if len(counts) == 2:
    behind, ahead = int(counts[0]), int(counts[1])

widgets = []
if changed:
    widgets.append({"type": "text", "icon": "pencil", "text": str(changed), "tooltip": f"{changed} changed file(s)"})
if ahead:
    widgets.append({"type": "text", "icon": "arrow-up", "text": str(ahead), "tooltip": f"{ahead} commit(s) to push"})
if behind:
    widgets.append({"type": "text", "icon": "arrow-down", "text": str(behind), "tooltip": f"{behind} commit(s) to pull"})
if not widgets:
    widgets.append({"type": "text", "icon": "check", "text": "clean", "tooltip": "Nothing to commit, nothing to sync"})

# a quiet repository is checked less often
print(json.dumps({"value": changed, "ttl": 10 if changed else 30, "widgets": widgets}))
