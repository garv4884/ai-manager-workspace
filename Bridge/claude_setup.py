"""One-click connection of Claude Code to the AI Manager Bridge (used by the bridge window and `connect-claude`).

Adds three hooks to your Claude Code user settings (~/.claude/settings.json):
  PermissionRequest  "Allow this command?" walks up to your desk in VR
  UserPromptSubmit   the agent's desk shows it is working
  Stop               the desk shows it is idle again
Every Claude Code session then appears as an agent named after its project folder. Your own settings are
kept; a backup is written once; hooks added by us are tagged "--managed" so they can be removed cleanly.
"""
import json
import os
import shutil
import sys

MARK = "--managed"


def settings_path():
    return os.path.join(os.path.expanduser("~"), ".claude", "settings.json")


def hook_command():
    """The command Claude Code runs: this program with the `hook` subcommand."""
    if getattr(sys, "frozen", False):
        return f'"{sys.executable}" hook {MARK}'
    return f'"{sys.executable}" "{os.path.join(os.path.dirname(os.path.abspath(__file__)), "aim_bridge.py")}" hook {MARK}'


def _load():
    try:
        with open(settings_path(), encoding="utf-8") as f:
            data = json.load(f)
        return data if isinstance(data, dict) else {}
    except FileNotFoundError:
        return {}


def _save(data):
    path = settings_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    backup = path + ".aim-backup"
    if os.path.exists(path) and not os.path.exists(backup):
        shutil.copyfile(path, backup)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    os.replace(tmp, path)


def _ours(entry):
    return any(MARK in (h.get("command") or "") for h in entry.get("hooks", []) if isinstance(h, dict))


def is_connected():
    hooks = _load().get("hooks", {})
    return isinstance(hooks, dict) and any(_ours(e) for e in hooks.get("PermissionRequest", []) if isinstance(e, dict))


def connect():
    data = _load()
    hooks = data.setdefault("hooks", {})
    cmd = hook_command()
    for event, timeout in (("PermissionRequest", 600), ("UserPromptSubmit", None), ("Stop", None)):
        entries = [e for e in hooks.get(event, []) if isinstance(e, dict) and not _ours(e)]
        hook = {"type": "command", "command": cmd}
        if timeout:
            hook["timeout"] = timeout
        entries.append({"hooks": [hook]})
        hooks[event] = entries
    _save(data)


def disconnect():
    data = _load()
    hooks = data.get("hooks", {})
    for event in list(hooks):
        kept = [e for e in hooks[event] if isinstance(e, dict) and not _ours(e)]
        if kept:
            hooks[event] = kept
        else:
            del hooks[event]
    if not hooks:
        data.pop("hooks", None)
    _save(data)
