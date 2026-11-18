#!/usr/bin/env python3
"""
Mock LLM server for testing AI Manager Workspace without real keys or cost.

It speaks the wire formats of Anthropic (/v1/messages), OpenAI and OpenAI-compatible servers
(/v1/chat/completions) and Google Gemini (/v1beta/models/<m>:generateContent). It checks the auth
header and request shape each provider requires, then answers like the real API would: team plans for
"split the goal" requests, deliverables for work, plain text for wrap-ups. Every call is logged.

    python Bridge/mock_llm.py            # http://127.0.0.1:8899
Point a provider's base URL at it, e.g. Anthropic: http://127.0.0.1:8899, OpenAI: http://127.0.0.1:8899/v1,
Gemini: http://127.0.0.1:8899, Custom: http://127.0.0.1:8899/v1
"""
import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LOG = []


def reply_for(provider, system, user, wants_json):
    """Builds the model's answer (a JSON string when a schema was requested)."""
    if "Split the manager's goal" in system:
        names = re.findall(r"^- ([^(]+?) \(current role", user, re.M)
        roles = ["Researcher", "Writer", "Designer", "Analyst", "Reviewer", "Engineer", "Tester", "Planner"]
        goal = re.search(r"Goal: (.*)", user)
        goal = goal.group(1).strip() if goal else "the goal"
        assignments = [{"agent": n.strip(), "role": roles[i % len(roles)], "title": f"{roles[i % len(roles)]} part",
                        "task": f"Handle the {roles[i % len(roles)].lower()} part of: {goal[:60]}",
                        "needs": "choice" if i == 1 else "approval"} for i, n in enumerate(names)]
        return json.dumps({"assignments": assignments})
    if "wrap-up report" in system:
        return f"[{provider}] Wrap-up: the team delivered every part of the goal. Next step: ship it."
    if "Connection test" in user:
        return "OK"
    who = re.search(r"You are (\w+)", system)
    who = who.group(1) if who else "agent"
    if wants_json:
        may_ask = "If the task is ambiguous" in system
        if may_ask and "choice" in user.lower() and "Manager answered" not in user:
            return json.dumps({"kind": "choice", "text": f"{who} via {provider}: which direction?", "options": ["Fast", "Thorough"]})
        return json.dumps({"kind": "deliverable", "text": f"{who} via {provider}: done.\n1. Did the work.\n2. Checked it.", "options": []})
    return f"{who} via {provider}: done."


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def send(self, code, obj):
        data = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == "/log":
            return self.send(200, LOG)
        self.send(404, {"error": {"message": "not found"}})

    def do_POST(self):
        n = int(self.headers.get("Content-Length") or 0)
        try:
            body = json.loads(self.rfile.read(n) or b"{}")
        except json.JSONDecodeError:
            return self.send(400, {"error": {"message": "body is not JSON"}})
        path = self.path

        if path == "/v1/messages":
            if not self.headers.get("x-api-key") or not self.headers.get("anthropic-version"):
                return self.send(401, {"error": {"message": "missing x-api-key / anthropic-version"}})
            for k in ("model", "max_tokens", "messages"):
                if k not in body:
                    return self.send(400, {"error": {"message": f"missing {k}"}})
            system = body.get("system", "")
            user = body["messages"][0]["content"]
            wants = "format" in (body.get("output_config") or {})
            text = reply_for("anthropic", system, user, wants)
            LOG.append({"provider": "anthropic", "model": body["model"], "json": wants, "agent": re.findall(r"You are (\w+)", system)[:1]})
            return self.send(200, {"content": [{"type": "text", "text": text}], "stop_reason": "end_turn"})

        if path.endswith("/chat/completions"):
            auth = self.headers.get("Authorization", "")
            msgs = body.get("messages") or []
            if "model" not in body or len(msgs) < 2 or msgs[0].get("role") != "system":
                return self.send(400, {"error": {"message": "need model + system/user messages"}})
            rf = body.get("response_format") or {}
            provider = "openai" if rf.get("type") == "json_schema" or "max_completion_tokens" in body else "custom"
            if provider == "openai" and not auth.startswith("Bearer "):
                return self.send(401, {"error": {"message": "missing bearer token"}})
            wants = bool(rf)
            text = reply_for(provider, msgs[0]["content"], msgs[1]["content"], wants)
            LOG.append({"provider": provider, "model": body["model"], "json": wants, "agent": re.findall(r"You are (\w+)", msgs[0]["content"])[:1]})
            return self.send(200, {"choices": [{"message": {"role": "assistant", "content": text}, "finish_reason": "stop"}]})

        m = re.match(r"^/v1beta/models/([^:]+):generateContent$", path)
        if m:
            if not self.headers.get("x-goog-api-key"):
                return self.send(403, {"error": {"message": "missing x-goog-api-key"}})
            gen = body.get("generationConfig") or {}
            if "additionalProperties" in json.dumps(gen.get("responseSchema", {})):
                return self.send(400, {"error": {"message": "additionalProperties is not supported in responseSchema"}})
            system = body["systemInstruction"]["parts"][0]["text"]
            user = body["contents"][0]["parts"][0]["text"]
            wants = gen.get("responseMimeType") == "application/json"
            text = reply_for("gemini", system, user, wants)
            LOG.append({"provider": "gemini", "model": m.group(1), "json": wants, "agent": re.findall(r"You are (\w+)", system)[:1]})
            return self.send(200, {"candidates": [{"content": {"parts": [{"text": text}], "role": "model"}, "finishReason": "STOP"}]})

        self.send(404, {"error": {"message": f"no route {path}"}})


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8899
    print(f"Mock LLM on http://127.0.0.1:{port}  (GET /log for the call log)")
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
