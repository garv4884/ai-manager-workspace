#!/usr/bin/env python3
"""
AI Manager Bridge: connects real agents on your computer to the AI Manager Workspace in VR.

Agents (Claude Code sessions, scripts, CI jobs, anything that can make an HTTP call or run the
`aim` CLI) register here and send requests: "approve this plan", "which option?", "here is my
report". The headset picks them up, an agent walks your request to the desk, and your decision
(thumbs up, an answer, a chosen option...) is sent back to the agent that is waiting for it.
The headset can also hand tasks to these agents (desk card or "Brief the team").

Python 3.8+ standard library only. Run:

    python aim_bridge.py                 # http://127.0.0.1:8787  (headset over USB: adb reverse)
    python aim_bridge.py --lan           # also reachable on Wi-Fi; non-local clients need the token

Open http://127.0.0.1:8787 for a browser dashboard (works without the headset).
"""
import argparse
import json
import os
import secrets
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

VERSION = "1.0"
KINDS = {"approval", "question", "choice", "report", "error"}
ACTIONS = {"approve", "changes", "reject", "answer", "decide", "choose", "retry", "ack", "followup"}
POSITIVE = {"approve", "answer", "decide", "choose", "retry", "ack", "followup"}
AGENT_TIMEOUT = 15 * 60          # forget agents silent for this long
MAX_EVENTS = 2000
MAX_BODY = 64 * 1024             # bytes per request body
KEEP_FINISHED = 300              # decided / cancelled requests kept for the dashboard


def now():
    return time.time()


class State:
    """All bridge state, guarded by one condition variable (long-polls wait on it)."""

    def __init__(self):
        self.cv = threading.Condition()
        self.seq = 0
        self.events = []                 # [{seq, type, data}]
        self.agents = {}                 # name -> dict
        self.requests = {}               # id -> dict
        self.tasks = {}                  # agent -> [task]
        self.headset_seen = 0.0

    # -- helpers (call with cv held) ----------------------------------------------------------
    def _emit(self, type_, data):
        self.seq += 1
        self.events.append({"seq": self.seq, "type": type_, "data": data})
        if len(self.events) > MAX_EVENTS:
            del self.events[: len(self.events) - MAX_EVENTS]
        self.cv.notify_all()

    def _touch_agent(self, name, **fields):
        a = self.agents.get(name)
        is_new = a is None
        if is_new:
            a = {"name": name, "role": "", "kind": "agent", "state": "idle", "title": "", "progress": -1, "cwd": ""}
            self.agents[name] = a
        changed = is_new
        for k, v in fields.items():
            if v is not None and a.get(k) != v:
                a[k] = v
                changed = True
        a["lastSeen"] = now()
        if changed:
            self._emit("agent", dict(a))
        return a

    def _expire(self):
        finished = sorted((r for r in self.requests.values() if r["state"] != "pending"), key=lambda r: r["created"])
        for r in finished[: max(0, len(finished) - KEEP_FINISHED)]:
            del self.requests[r["id"]]
        cutoff = now() - AGENT_TIMEOUT
        for name in [n for n, a in self.agents.items() if a["lastSeen"] < cutoff and not self._pending_for(n)]:
            del self.agents[name]
            self._emit("agent_gone", {"name": name})

    def _pending_for(self, name):
        return any(r["agent"] == name and r["state"] == "pending" for r in self.requests.values())

    # -- agent side ---------------------------------------------------------------------------
    def register(self, body):
        with self.cv:
            return self._touch_agent(body["agent"], role=body.get("role"), kind=body.get("kind"), cwd=body.get("cwd"))

    def status(self, body):
        with self.cv:
            return self._touch_agent(body["agent"], state=body.get("state", "working"), title=body.get("title", ""),
                                     progress=float(body.get("progress", -1)), role=body.get("role"), kind=body.get("kind"))

    def new_request(self, body):
        kind = body.get("kind", "approval")
        if kind not in KINDS:
            raise ValueError(f"kind must be one of {sorted(KINDS)}")
        options = [str(o) for o in body.get("options") or []][:4]
        if kind == "choice" and len(options) < 2:
            raise ValueError("a choice needs at least 2 options")
        with self.cv:
            self._touch_agent(body["agent"], role=body.get("role"), kind=body.get("agentKind"), state="waiting")
            rid = uuid.uuid4().hex[:10]
            req = {
                "id": rid, "agent": body["agent"], "kind": kind,
                "title": str(body.get("title") or kind.title())[:120],
                "text": str(body.get("text") or "")[:6000],
                "options": options, "task": body.get("task") or "",
                "state": "pending", "decision": None, "created": now(),
            }
            self.requests[rid] = req
            self._emit("request", self._public(req))
            return self._public(req)

    def wait_request(self, rid, wait):
        deadline = now() + wait
        with self.cv:
            while True:
                req = self.requests.get(rid)
                if req is None:
                    return None
                if req["state"] != "pending" or now() >= deadline:
                    a = self.agents.get(req["agent"])
                    if a:
                        a["lastSeen"] = now()
                    return self._public(req)
                self.cv.wait(timeout=max(0.1, deadline - now()))

    def cancel_request(self, rid):
        with self.cv:
            req = self.requests.get(rid)
            if req is None or req["state"] != "pending":
                return req and self._public(req)
            req["state"] = "cancelled"
            self._emit("cancel", {"id": rid, "agent": req["agent"]})
            return self._public(req)

    def next_task(self, agent, wait):
        deadline = now() + wait
        with self.cv:
            self._touch_agent(agent)
            while True:
                queue = self.tasks.get(agent) or []
                if queue:
                    task = queue.pop(0)
                    return task
                if now() >= deadline:
                    return None
                self.cv.wait(timeout=max(0.1, deadline - now()))

    # -- headset / dashboard side -------------------------------------------------------------
    def events_since(self, since, wait):
        deadline = now() + wait
        with self.cv:
            self.headset_seen = now()
            self._expire()
            if since <= 0 or (self.events and since < self.events[0]["seq"] - 1):
                return {"seq": self.seq, "snapshot": True, "events": self._snapshot()}
            while True:
                fresh = [e for e in self.events if e["seq"] > since]
                if fresh or now() >= deadline:
                    return {"seq": self.seq, "snapshot": False, "events": fresh}
                self.cv.wait(timeout=max(0.1, deadline - now()))

    def _snapshot(self):
        evs = [{"seq": self.seq, "type": "agent", "data": dict(a)} for a in self.agents.values()]
        evs += [{"seq": self.seq, "type": "request", "data": self._public(r)}
                for r in sorted(self.requests.values(), key=lambda r: r["created"]) if r["state"] == "pending"]
        return evs

    def decide(self, body):
        action = body.get("action")
        if action not in ACTIONS:
            raise ValueError(f"action must be one of {sorted(ACTIONS)}")
        with self.cv:
            req = self.requests.get(body.get("id"))
            if req is None:
                raise KeyError("unknown request")
            if req["state"] != "pending":
                return self._public(req)
            req["state"] = "decided"
            req["decision"] = {"action": action, "text": str(body.get("text") or ""), "approved": action in POSITIVE,
                               "at": now()}
            self._touch_agent(req["agent"], state="working")
            self._emit("decided", {"id": req["id"], "agent": req["agent"], "action": action})
            return self._public(req)

    def assign_task(self, body):
        agent = body["agent"]
        with self.cv:
            self._touch_agent(agent)
            task = {"id": body.get("id") or uuid.uuid4().hex[:10], "title": str(body.get("title") or "Task")[:120],
                    "text": str(body.get("text") or ""), "goal": body.get("goal") or "", "created": now()}
            self.tasks.setdefault(agent, []).append(task)
            self._emit("task", {"agent": agent, "task": task})
            return task

    def summary(self):
        with self.cv:
            self._expire()
            return {
                "version": VERSION,
                "headsetConnected": now() - self.headset_seen < 40,
                "agents": list(self.agents.values()),
                "pending": [self._public(r) for r in self.requests.values() if r["state"] == "pending"],
                "recent": [self._public(r) for r in sorted(self.requests.values(), key=lambda r: -r["created"])[:30]
                           if r["state"] != "pending"],
                "queuedTasks": {k: len(v) for k, v in self.tasks.items() if v},
            }

    @staticmethod
    def _public(req):
        return {k: req[k] for k in ("id", "agent", "kind", "title", "text", "options", "task", "state", "decision", "created")}


STATE = State()
TOKEN = ""
LAN = False


class Handler(BaseHTTPRequestHandler):
    server_version = f"AIManagerBridge/{VERSION}"
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        if os.environ.get("AIM_VERBOSE"):
            super().log_message(fmt, *args)

    # -- plumbing -----------------------------------------------------------------------------
    def _send(self, code, payload, content_type="application/json"):
        data = payload if isinstance(payload, bytes) else json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _body(self):
        n = int(self.headers.get("Content-Length") or 0)
        if n <= 0:
            return {}
        if n > MAX_BODY:
            raise ValueError("request body too large")
        body = json.loads(self.rfile.read(n).decode("utf-8") or "{}")
        if not isinstance(body, dict):
            raise ValueError("body must be a JSON object")
        return body

    def _local(self):
        return self.client_address[0] in ("127.0.0.1", "::1")

    def _safe_origin(self, method):
        """Blocks web pages in your browser from driving the bridge (CSRF / DNS rebinding)."""
        host = (self.headers.get("Host") or "").split(":")[0].strip("[]").lower()
        if not LAN and host not in ("127.0.0.1", "localhost", "::1"):
            return False                                   # DNS rebinding: a foreign name pointing at 127.0.0.1
        origin = self.headers.get("Origin")
        if origin:
            o = urlparse(origin)
            if (o.hostname or "") not in ("127.0.0.1", "localhost", "::1") or (o.port or 80) != self.server.server_port:
                return False                               # cross-site request from some web page
        if method in ("POST", "DELETE"):
            ctype = (self.headers.get("Content-Type") or "").split(";")[0].strip().lower()
            if method == "POST" and ctype != "application/json":
                return False                               # forces a CORS preflight, which we never grant
        return True

    def _authorised(self):
        if not LAN or self._local():
            return True
        q = parse_qs(urlparse(self.path).query)
        given = self.headers.get("X-AIM-Token") or (q.get("token") or [""])[0]
        return bool(TOKEN) and secrets.compare_digest(given, TOKEN)

    def _route(self, method):
        if not self._safe_origin(method):
            return self._send(403, {"error": "cross-origin or non-JSON requests are not allowed"})
        if not self._authorised():
            return self._send(401, {"error": "missing or wrong token (X-AIM-Token header)"})
        url = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(url.query).items()}
        path = url.path.rstrip("/") or "/"
        try:
            wait = min(float(q.get("wait", 0)), 120.0)
            if method == "GET" and path == "/":
                return self._send(200, DASHBOARD.encode(), "text/html; charset=utf-8")
            if method == "GET" and path == "/api/health":
                return self._send(200, {"ok": True, "version": VERSION})
            if method == "GET" and path == "/api/state":
                return self._send(200, STATE.summary())
            if method == "GET" and path == "/api/events":
                return self._send(200, STATE.events_since(int(q.get("since", 0)), wait))
            if method == "POST" and path == "/api/agents":
                return self._send(200, STATE.register(self._need(self._body(), "agent")))
            if method == "POST" and path == "/api/status":
                return self._send(200, STATE.status(self._need(self._body(), "agent")))
            if method == "POST" and path == "/api/requests":
                return self._send(200, STATE.new_request(self._need(self._body(), "agent")))
            if path.startswith("/api/requests/"):
                rid = path.rsplit("/", 1)[1]
                if method == "GET":
                    r = STATE.wait_request(rid, wait)
                    return self._send(200 if r else 404, r or {"error": "unknown request"})
                if method == "DELETE":
                    r = STATE.cancel_request(rid)
                    return self._send(200 if r else 404, r or {"error": "unknown request"})
            if method == "POST" and path == "/api/decisions":
                return self._send(200, STATE.decide(self._body()))
            if method == "POST" and path == "/api/tasks":
                return self._send(200, STATE.assign_task(self._need(self._body(), "agent")))
            if method == "GET" and path == "/api/tasks/next":
                t = STATE.next_task(q["agent"], wait)
                return self._send(200, {"task": t})
            return self._send(404, {"error": f"no route {method} {path}"})
        except KeyError as e:
            return self._send(400, {"error": f"missing {e}"})
        except (ValueError, TypeError, AttributeError, json.JSONDecodeError) as e:
            return self._send(400, {"error": str(e)})

    @staticmethod
    def _need(body, *keys):
        for k in keys:
            if not body.get(k):
                raise KeyError(k)
        body[keys[0]] = str(body[keys[0]]).strip()[:60]
        return body

    def do_GET(self):
        self._route("GET")

    def do_POST(self):
        self._route("POST")

    def do_DELETE(self):
        self._route("DELETE")


DASHBOARD = r"""<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>AI Manager Bridge</title><style>
:root{--bg:#0e0f12;--card:#17191e;--line:#262a31;--text:#eef0f3;--muted:#9aa3b2;--teal:#4fd1c5}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.45 system-ui,Segoe UI,Roboto,sans-serif}
header{padding:18px 20px;border-bottom:1px solid var(--line);display:flex;gap:14px;align-items:center;flex-wrap:wrap}
h1{font-size:18px;margin:0;letter-spacing:.04em}.pill{padding:3px 10px;border-radius:99px;font-size:12px;background:var(--line);color:var(--muted)}
.ok{background:#12352f;color:var(--teal)}main{display:grid;grid-template-columns:1fr 1fr;gap:16px;padding:16px 20px}
@media(max-width:800px){main{grid-template-columns:1fr}}section{background:var(--card);border:1px solid var(--line);border-radius:16px;padding:14px 16px}
h2{font-size:13px;letter-spacing:.12em;color:var(--teal);margin:0 0 10px}.item{border-top:1px solid var(--line);padding:10px 0}
.item:first-of-type{border-top:0}.who{color:var(--muted);font-size:13px}pre{white-space:pre-wrap;margin:6px 0;font:13px/1.4 ui-monospace,Consolas,monospace;color:#cfd5de}
button{background:#2a2f37;color:var(--text);border:0;border-radius:99px;padding:7px 14px;margin:4px 6px 0 0;cursor:pointer;font-weight:600}
button.go{background:#13675f}button.no{background:#5a2328}input{background:#0b0c0f;color:var(--text);border:1px solid var(--line);border-radius:10px;padding:7px 10px;width:100%;margin-top:6px}
.empty{color:var(--muted)}</style></head><body>
<header><h1>AI MANAGER BRIDGE</h1><span id="hs" class="pill">headset: offline</span><span class="pill">decide here or in VR</span></header>
<main><section><h2>WAITING FOR YOU</h2><div id="pending"></div></section>
<section><h2>AGENTS</h2><div id="agents"></div><h2 style="margin-top:16px">RECENT DECISIONS</h2><div id="recent"></div></section></main>
<script>
function el(tag,cls,text){const e=document.createElement(tag);if(cls)e.className=cls;if(text!==undefined)e.textContent=text;return e}
function btn(label,cls,fn){const b=el('button',cls,label);b.addEventListener('click',fn);return b}
async function post(p,b){await fetch(p,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(b)});load()}
const decide=(id,action,text)=>post('/api/decisions',{id,action,text:text||''});
function empty(box,msg){box.replaceChildren(el('div','empty',msg))}
async function load(){
 const s=await (await fetch('/api/state')).json();
 const hs=document.getElementById('hs');hs.textContent='headset: '+(s.headsetConnected?'connected':'offline');hs.className='pill'+(s.headsetConnected?' ok':'');
 const pend=document.getElementById('pending');
 if(!s.pending.length)empty(pend,'Nothing waiting.');else pend.replaceChildren(...s.pending.map(r=>{
  const d=el('div','item');const h=el('div');h.append(el('b','',r.title),' ',el('span','who',r.agent+' · '+r.kind));d.append(h,el('pre','',r.text));
  const inp=el('input');
  if(r.kind=='approval'){inp.placeholder='feedback';d.append(btn('Approve','go',()=>decide(r.id,'approve')),inp,btn('Request changes','',()=>decide(r.id,'changes',inp.value)),btn('Reject','no',()=>decide(r.id,'reject')))}
  else if(r.kind=='question'){inp.placeholder='your answer';d.append(inp,btn('Answer','go',()=>decide(r.id,'answer',inp.value)),btn('You decide','',()=>decide(r.id,'decide')),btn('Drop','no',()=>decide(r.id,'reject')))}
  else if(r.kind=='choice'){r.options.forEach(o=>d.append(btn(o,'go',()=>decide(r.id,'choose',o))));d.append(btn('Drop','no',()=>decide(r.id,'reject')))}
  else if(r.kind=='error'){d.append(btn('Retry','go',()=>decide(r.id,'retry')),btn('Drop','no',()=>decide(r.id,'reject')))}
  else d.append(btn('Got it','go',()=>decide(r.id,'ack')));
  return d}));
 const ag=document.getElementById('agents');
 if(!s.agents.length)empty(ag,'No agents yet. Try: python aim.py ask "Hello?"');else ag.replaceChildren(...s.agents.map(a=>{
  const d=el('div','item');const h=el('div');h.append(el('b','',a.name),' ',el('span','who',[a.kind,a.role,a.state].filter(Boolean).join(' · ')));
  const inp=el('input');inp.placeholder='give '+a.name+' a task, press Enter';
  inp.addEventListener('keydown',e=>{if(e.key=='Enter'&&inp.value)post('/api/tasks',{agent:a.name,title:inp.value.slice(0,60),text:inp.value})});
  d.append(h,el('div','who',a.title||''),inp);return d}));
 const rec=document.getElementById('recent');
 if(!s.recent.length)empty(rec,'None yet.');else rec.replaceChildren(...s.recent.map(r=>{
  const d=el('div','item');d.append(el('span','who',r.agent+'  '),r.title+' → ',el('b','',r.decision?r.decision.action:r.state));return d}));
}
load();setInterval(()=>{if(!document.activeElement||document.activeElement.tagName!='INPUT')load()},1500);
</script></body></html>"""


def main():
    global TOKEN, LAN
    ap = argparse.ArgumentParser(description="AI Manager Bridge")
    ap.add_argument("--port", type=int, default=int(os.environ.get("AIM_PORT", 8787)))
    ap.add_argument("--lan", action="store_true", help="listen on all interfaces (Quest over Wi-Fi); requires the token")
    args = ap.parse_args()
    LAN = args.lan

    token_file = os.path.join(os.path.expanduser("~"), ".aim", "token")
    os.makedirs(os.path.dirname(token_file), exist_ok=True)
    if not os.path.exists(token_file):
        with open(token_file, "w") as f:
            f.write(secrets.token_urlsafe(18))
    with open(token_file) as f:
        TOKEN = f.read().strip()

    host = "0.0.0.0" if LAN else "127.0.0.1"
    server = ThreadingHTTPServer((host, args.port), Handler)
    server.daemon_threads = True
    print(f"AI Manager Bridge {VERSION} on http://127.0.0.1:{args.port}  (dashboard in your browser)")
    if LAN:
        print(f"LAN mode: Wi-Fi clients must send the token from {token_file}")
    else:
        print("Quest over USB:  adb reverse tcp:%d tcp:%d   (or Tools > AI Manager > Quest API Key > Connect bridge)" % (args.port, args.port))
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
