#!/usr/bin/env python3
"""
aim: talk to your AI Manager (in VR) from any agent, script or terminal.

  aim ask      "Which database should I use?" [--options "Postgres,SQLite"]   -> prints the answer
  aim approve  "Deploy plan" --file plan.md                                    -> exit 0 approved, 1 changes, 2 rejected
  aim report   "Nightly tests" "312 passed, 2 flaky"                           -> FYI page (add --wait to wait for "got it")
  aim status   working "Refactoring auth" [--progress 0.4]
  aim next                                                                     -> waits for a task assigned in VR
  aim worker   --cwd ~/code/api [--cmd claude]                                 -> runs tasks from VR with Claude Code
  aim hook                                                                     -> Claude Code hook (permission prompts go to VR)
  aim mcp                                                                      -> MCP server (stdio) with manager tools

Agent name: --agent, else $AIM_AGENT, else the current folder name. Bridge: $AIM_URL (default
http://127.0.0.1:8787). Python standard library only.
"""
import argparse
import json
import os
import shlex
import subprocess
import sys
import time
import urllib.error
import urllib.request

URL = os.environ.get("AIM_URL", "http://127.0.0.1:8787").rstrip("/")
TOKEN = os.environ.get("AIM_TOKEN", "")
if not TOKEN:
    try:
        with open(os.path.join(os.path.expanduser("~"), ".aim", "token")) as _f:
            TOKEN = _f.read().strip()
    except OSError:
        pass


class BridgeDown(Exception):
    pass


def call(method, path, body=None, timeout=70):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(URL + path, data=data, method=method,
                                 headers={"Content-Type": "application/json", "X-AIM-Token": TOKEN})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        raise BridgeDown(f"bridge said {e.code}: {e.read().decode(errors='replace')}")
    except (urllib.error.URLError, OSError) as e:
        raise BridgeDown(f"bridge not reachable at {URL} ({e}). Start it: python aim_bridge.py")


def agent_name(args):
    name = getattr(args, "agent", None) or os.environ.get("AIM_AGENT") or os.path.basename(os.getcwd()) or "agent"
    return name[:40]


def request(agent, kind, title, text="", options=None, task="", role=None, agent_kind=None, timeout=None, on_tick=None):
    """Posts a request and blocks until the manager decides. Returns the decision dict, or None on timeout/cancel."""
    r = call("POST", "/api/requests", {"agent": agent, "kind": kind, "title": title, "text": text,
                                       "options": options or [], "task": task, "role": role, "agentKind": agent_kind})
    rid = r["id"]
    deadline = time.time() + timeout if timeout else None
    try:
        while True:
            wait = 50 if deadline is None else max(1, min(50, int(deadline - time.time())))
            r = call("GET", f"/api/requests/{rid}?wait={wait}", timeout=wait + 20)
            if r.get("state") == "decided":
                return r["decision"]
            if r.get("state") == "cancelled":
                return None
            if deadline and time.time() >= deadline:
                call("DELETE", f"/api/requests/{rid}")
                return None
            if on_tick:
                on_tick()
    except KeyboardInterrupt:
        try:
            call("DELETE", f"/api/requests/{rid}")
        finally:
            raise


def read_text(args):
    if getattr(args, "file", None):
        with open(args.file, encoding="utf-8", errors="replace") as f:
            return f.read()
    if getattr(args, "text", None):
        return args.text
    if not sys.stdin.isatty():
        return sys.stdin.read()
    return ""


# ── simple commands ──────────────────────────────────────────────────────────────────────────

def cmd_ask(args):
    opts = [o.strip() for o in (args.options or "").split(",") if o.strip()]
    d = request(agent_name(args), "choice" if len(opts) >= 2 else "question", args.title or "Question",
                args.question, opts, role=args.role, agent_kind="CLI", timeout=args.timeout)
    if d is None:
        print("(no answer)", file=sys.stderr)
        return 3
    if d["action"] == "reject":
        print("(dropped by the manager)", file=sys.stderr)
        return 2
    print(d["text"] or "Use your best judgment.")
    return 0


def cmd_approve(args):
    d = request(agent_name(args), "approval", args.title, read_text(args), role=args.role, agent_kind="CLI", timeout=args.timeout)
    if d is None:
        print("(no decision)", file=sys.stderr)
        return 3
    if d["action"] == "approve":
        print("APPROVED")
        return 0
    if d["action"] == "changes":
        print("CHANGES REQUESTED: " + d["text"])
        return 1
    print("REJECTED")
    return 2


def cmd_report(args):
    agent = agent_name(args)
    if not args.wait:
        r = call("POST", "/api/requests", {"agent": agent, "kind": "report", "title": args.title, "text": args.text or read_text(args),
                                           "role": args.role, "agentKind": "CLI"})
        print(r["id"])
        return 0
    d = request(agent, "report", args.title, args.text or read_text(args), role=args.role, agent_kind="CLI", timeout=args.timeout)
    if d and d["action"] == "followup" and d["text"]:
        print("FOLLOW-UP: " + d["text"])
    return 0


def cmd_status(args):
    call("POST", "/api/status", {"agent": agent_name(args), "state": args.state, "title": args.title or "",
                                 "progress": args.progress if args.progress is not None else -1,
                                 "role": args.role, "kind": args.kind})
    return 0


def cmd_register(args):
    call("POST", "/api/agents", {"agent": agent_name(args), "role": args.role, "kind": args.kind, "cwd": os.getcwd()})
    print(f"registered {agent_name(args)}")
    return 0


def cmd_next(args):
    agent = agent_name(args)
    call("POST", "/api/agents", {"agent": agent, "role": args.role, "kind": args.kind or "script", "cwd": os.getcwd()})
    deadline = time.time() + args.timeout if args.timeout else None
    while True:
        r = call("GET", f"/api/tasks/next?agent={urllib.request.quote(agent)}&wait=50", timeout=80)
        if r.get("task"):
            print(json.dumps(r["task"]) if args.json else r["task"]["text"])
            return 0
        if deadline and time.time() >= deadline:
            return 3


# ── worker: tasks from VR, done by Claude Code (or any command) ──────────────────────────────

def run_agent_cmd(cmd, prompt, cwd, session):
    """Runs one turn. For `claude`, keeps the session so revisions continue the same conversation."""
    parts = shlex.split(cmd, posix=os.name != "nt")
    is_claude = os.path.basename(parts[0]).lower().startswith("claude")
    if is_claude:
        argv = parts + ["-p", prompt, "--output-format", "json"]
        if session:
            argv += ["--resume", session]
    else:
        argv = parts
    p = subprocess.run(argv, cwd=cwd, input=None if is_claude else prompt, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", shell=False)
    out = (p.stdout or "").strip()
    if is_claude:
        try:
            j = json.loads(out)
            return j.get("result") or "", j.get("session_id") or session, p.returncode == 0 and not j.get("is_error")
        except json.JSONDecodeError:
            pass
    return out or (p.stderr or "").strip(), session, p.returncode == 0


def cmd_worker(args):
    agent = agent_name(args)
    cwd = os.path.abspath(os.path.expanduser(args.cwd or os.getcwd()))
    kind = args.kind or ("Claude Code" if "claude" in args.cmd else "worker")
    call("POST", "/api/agents", {"agent": agent, "role": args.role, "kind": kind, "cwd": cwd})
    print(f"[{agent}] waiting for tasks from the manager (folder: {cwd}) ... Ctrl+C to stop")
    while True:
        try:
            call("POST", "/api/status", {"agent": agent, "state": "idle", "title": "Ready for a task", "kind": kind})
            r = call("GET", f"/api/tasks/next?agent={urllib.request.quote(agent)}&wait=50", timeout=80)
        except BridgeDown as e:
            print(f"[{agent}] {e}; retrying in 5 s")
            time.sleep(5)
            continue
        task = r.get("task")
        if not task:
            continue
        print(f"[{agent}] task: {task['title']}")
        prompt = task["text"]
        if task.get("goal"):
            prompt = f"Team goal: {task['goal']}\n\nYour task: {prompt}"
        prompt += ("\n\nWork in this folder. When done, reply with a short summary for your manager "
                   "(what you did, what changed, anything they must check), under 150 words.")
        session = None
        revision = 1
        while True:
            call("POST", "/api/status", {"agent": agent, "state": "working", "title": task["title"], "kind": kind})
            summary, session, ok = run_agent_cmd(args.cmd, prompt, cwd, session)
            title = task["title"] + (f" (rev {revision})" if revision > 1 else "")
            d = request(agent, "approval" if ok else "error", title, summary[:6000] or "(no output)", task=task["id"],
                        role=args.role, agent_kind=kind)
            if d is None or d["action"] in ("approve", "ack", "reject"):
                print(f"[{agent}] {task['title']}: {d['action'] if d else 'no decision'}")
                break
            revision += 1
            prompt = ("The manager reviewed your work and asked for changes: " + (d["text"] or "please improve it")
                      if d["action"] == "changes" else "The manager says: retry the task." if d["action"] == "retry"
                      else d["text"] or "Continue.")


# ── Claude Code hook: permission prompts and notifications go to VR ──────────────────────────

def cmd_hook(args):
    try:
        data = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0
    event = data.get("hook_event_name", "")
    agent = args.agent or os.environ.get("AIM_AGENT") or os.path.basename(data.get("cwd") or os.getcwd())
    try:
        if event in ("PermissionRequest", "PreToolUse"):
            tool = data.get("tool_name", "tool")
            ti = data.get("tool_input") or {}
            what = ti.get("command") or ti.get("file_path") or ti.get("url") or json.dumps(ti)[:800]
            desc = ti.get("description") or ""
            text = f"{tool}: {what}" + (f"\n\n{desc}" if desc else "") + f"\n\nFolder: {data.get('cwd', '')}"
            d = request(agent, "approval", f"Allow {tool}?", text, role=args.role, agent_kind="Claude Code",
                        timeout=args.timeout)
            if d is None:
                return 0                           # no decision in time: fall back to the normal prompt
            allow = d["action"] == "approve"
            reason = d["text"] or ("Approved by the manager in VR" if allow else "Declined by the manager in VR")
            if event == "PermissionRequest":
                decision = {"behavior": "allow"} if allow else {"behavior": "deny", "message": reason}
                out = {"hookSpecificOutput": {"hookEventName": "PermissionRequest", "decision": decision}}
            else:
                out = {"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                              "permissionDecision": "allow" if allow else "deny",
                                              "permissionDecisionReason": reason}}
            print(json.dumps(out))
        elif event == "Notification":
            call("POST", "/api/status", {"agent": agent, "state": "waiting", "title": data.get("message", "")[:120],
                                         "kind": "Claude Code"})
        elif event == "UserPromptSubmit":
            call("POST", "/api/status", {"agent": agent, "state": "working",
                                         "title": (data.get("prompt") or "Working")[:80], "kind": "Claude Code"})
        elif event == "Stop":
            if args.report_on_stop:                   # opt-in: a page per finished turn can flood the queue
                msg = data.get("last_assistant_message") or "Finished the current turn."
                if isinstance(msg, dict):
                    msg = json.dumps(msg)[:2000]
                call("POST", "/api/requests", {"agent": agent, "kind": "report", "title": "Turn finished",
                                               "text": str(msg)[:4000], "agentKind": "Claude Code"})
            call("POST", "/api/status", {"agent": agent, "state": "idle", "title": "Waiting for you", "kind": "Claude Code"})
    except BridgeDown:
        return 0                                   # never block Claude Code when the bridge is off
    return 0


# ── MCP server (stdio, JSON-RPC 2.0, newline-delimited) ──────────────────────────────────────

MCP_TOOLS = [
    {"name": "request_approval",
     "description": "Ask your human manager (in the AI Manager VR workspace) to approve a plan, change or result before you continue. Blocks until they decide. Returns APPROVED, CHANGES REQUESTED: <feedback>, or REJECTED.",
     "inputSchema": {"type": "object", "properties": {"title": {"type": "string", "description": "Short title, under 8 words"},
                                                      "details": {"type": "string", "description": "What you want approved, under 150 words"},
                                                      "task_id": {"type": "string", "description": "Task id from wait_for_task, if any"}},
                     "required": ["title", "details"]}},
    {"name": "ask_manager",
     "description": "Ask your human manager a question when you genuinely need their input. Give 2-4 short options when the answer is one of a few directions. Blocks until they answer.",
     "inputSchema": {"type": "object", "properties": {"question": {"type": "string"},
                                                      "options": {"type": "array", "items": {"type": "string"}}},
                     "required": ["question"]}},
    {"name": "report_to_manager",
     "description": "Send your manager an FYI update (finished work, findings, a summary). Does not block.",
     "inputSchema": {"type": "object", "properties": {"title": {"type": "string"}, "summary": {"type": "string"}},
                     "required": ["title", "summary"]}},
    {"name": "set_status",
     "description": "Show what you are working on, on your desk in the manager's VR workspace.",
     "inputSchema": {"type": "object", "properties": {"state": {"type": "string", "enum": ["working", "idle"]},
                                                      "title": {"type": "string"},
                                                      "progress": {"type": "number", "description": "0..1"}},
                     "required": ["state", "title"]}},
    {"name": "wait_for_task",
     "description": "Wait for the manager to assign you a task from VR. Returns the task (id, title, text) or 'no task yet'.",
     "inputSchema": {"type": "object", "properties": {"timeout_seconds": {"type": "number"}}}},
]


def mcp_call(agent, role, name, a):
    if name == "request_approval":
        d = request(agent, "approval", a.get("title", "Approval"), a.get("details", ""), task=a.get("task_id", ""),
                    role=role, agent_kind="MCP agent")
        if d is None:
            return "NO DECISION (timed out)"
        return {"approve": "APPROVED", "changes": "CHANGES REQUESTED: " + d["text"]}.get(d["action"], "REJECTED")
    if name == "ask_manager":
        opts = [str(o) for o in a.get("options") or []]
        d = request(agent, "choice" if len(opts) >= 2 else "question", "Question", a.get("question", ""), opts,
                    role=role, agent_kind="MCP agent")
        if d is None:
            return "NO ANSWER (timed out)"
        if d["action"] == "reject":
            return "The manager dropped this question; stop and summarise."
        return d["text"] or "Use your best judgment."
    if name == "report_to_manager":
        call("POST", "/api/requests", {"agent": agent, "kind": "report", "title": a.get("title", "Update"),
                                       "text": a.get("summary", ""), "role": role, "agentKind": "MCP agent"})
        return "Reported."
    if name == "set_status":
        call("POST", "/api/status", {"agent": agent, "state": a.get("state", "working"), "title": a.get("title", ""),
                                     "progress": a.get("progress", -1), "role": role, "kind": "MCP agent"})
        return "OK"
    if name == "wait_for_task":
        wait = int(min(max(a.get("timeout_seconds", 50) or 50, 1), 110))
        r = call("GET", f"/api/tasks/next?agent={urllib.request.quote(agent)}&wait={wait}", timeout=wait + 20)
        return json.dumps(r["task"]) if r.get("task") else "no task yet"
    raise ValueError(f"unknown tool {name}")


def cmd_mcp(args):
    agent = agent_name(args)
    try:
        call("POST", "/api/agents", {"agent": agent, "role": args.role, "kind": "MCP agent", "cwd": os.getcwd()})
    except BridgeDown:
        pass

    def send(msg):
        sys.stdout.write(json.dumps(msg) + "\n")
        sys.stdout.flush()

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue
        mid, method, params = msg.get("id"), msg.get("method"), msg.get("params") or {}
        if mid is None:
            continue                               # notifications (initialized, cancelled...)
        try:
            if method == "initialize":
                result = {"protocolVersion": params.get("protocolVersion", "2025-06-18"),
                          "capabilities": {"tools": {}},
                          "serverInfo": {"name": "ai-manager", "version": "1.0"},
                          "instructions": "Your work is reviewed by a human manager in VR. Use request_approval before "
                                          "risky or large changes, ask_manager when you need a decision, and "
                                          "report_to_manager when you finish."}
            elif method == "ping":
                result = {}
            elif method == "tools/list":
                result = {"tools": MCP_TOOLS}
            elif method == "tools/call":
                try:
                    text = mcp_call(agent, args.role, params.get("name"), params.get("arguments") or {})
                    result = {"content": [{"type": "text", "text": text}], "isError": False}
                except BridgeDown as e:
                    result = {"content": [{"type": "text", "text": str(e)}], "isError": True}
            else:
                send({"jsonrpc": "2.0", "id": mid, "error": {"code": -32601, "message": f"unknown method {method}"}})
                continue
            send({"jsonrpc": "2.0", "id": mid, "result": result})
        except Exception as e:                      # keep the server alive
            send({"jsonrpc": "2.0", "id": mid, "error": {"code": -32603, "message": str(e)}})
    return 0


def main():
    ap = argparse.ArgumentParser(prog="aim", description="Talk to your AI Manager in VR.")
    ap.add_argument("--agent", help="agent name shown in VR (default: $AIM_AGENT or folder name)")
    ap.add_argument("--role", help="role shown under the name, e.g. 'Backend dev'")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("ask"); p.add_argument("question"); p.add_argument("--options"); p.add_argument("--title")
    p.add_argument("--timeout", type=float); p.set_defaults(fn=cmd_ask)
    p = sub.add_parser("approve"); p.add_argument("title"); p.add_argument("text", nargs="?"); p.add_argument("--file")
    p.add_argument("--timeout", type=float); p.set_defaults(fn=cmd_approve)
    p = sub.add_parser("report"); p.add_argument("title"); p.add_argument("text", nargs="?"); p.add_argument("--file")
    p.add_argument("--wait", action="store_true"); p.add_argument("--timeout", type=float); p.set_defaults(fn=cmd_report)
    p = sub.add_parser("status"); p.add_argument("state", choices=["working", "idle", "waiting"]); p.add_argument("title", nargs="?")
    p.add_argument("--progress", type=float); p.add_argument("--kind"); p.set_defaults(fn=cmd_status)
    p = sub.add_parser("register"); p.add_argument("--kind", default="agent"); p.set_defaults(fn=cmd_register)
    p = sub.add_parser("next"); p.add_argument("--json", action="store_true"); p.add_argument("--kind")
    p.add_argument("--timeout", type=float); p.set_defaults(fn=cmd_next)
    p = sub.add_parser("worker"); p.add_argument("--cwd"); p.add_argument("--cmd", default="claude")
    p.add_argument("--kind"); p.set_defaults(fn=cmd_worker)
    p = sub.add_parser("hook"); p.add_argument("--timeout", type=float, default=540)
    p.add_argument("--report-on-stop", action="store_true", help="send a report page after every finished turn")
    p.add_argument("--managed", action="store_true", help=argparse.SUPPRESS)   # marks hooks installed by connect-claude
    p.set_defaults(fn=cmd_hook)
    p = sub.add_parser("mcp"); p.set_defaults(fn=cmd_mcp)

    args = ap.parse_args()
    try:
        sys.exit(args.fn(args) or 0)
    except BridgeDown as e:
        print(e, file=sys.stderr)
        sys.exit(4)
    except KeyboardInterrupt:
        sys.exit(130)


if __name__ == "__main__":
    main()
