# AI Manager Bridge

Run real agents on your PC and manage them from the VR workspace. Each agent gets a desk, walks
its requests up to you, and waits for your decision: thumbs up, an answer, a chosen option, or
"request changes". You can also hand tasks to these agents from VR, one at a time or as a whole
team brief.

**Setup takes about a minute and needs no IP address, no token and no cable:**

1. On your PC, double-click **AIManagerBridge.exe** (or run `python Bridge/aim_bridge.py`). A window shows a 6-digit **pairing code**.
2. In the headset: **Settings > Agent Bridge**. Your PC appears by itself (same Wi-Fi). Tap it and type the code. Done. The headset reconnects on its own from now on, even if the PC's address changes.
3. In the bridge window click **Connect Claude Code**. Every Claude Code session now shows up as an agent named after its project folder, and its "Allow this command?" prompts walk up to your desk.

Everything is Python standard library: nothing to install if you run from source.

```
 Claude Code ──hook/MCP──┐
 scripts / CI ──aim CLI──┼──►  AI Manager Bridge (PC, :8787)  ◄── Wi-Fi: auto-discovery + pairing code ──►  Quest app
 aim worker (claude -p) ─┘           │
                                     └── browser dashboard: http://127.0.0.1:8787
```

## Why use it

| You are... | Agents on desks | You do in VR |
|---|---|---|
| **Coding** across several repos or features | One Claude Code session per repo (hook or MCP), or `aim worker` per folder | Approve risky commands and plans, answer design questions, send changes back |
| **Researching** | Agents that read sources and write findings | Pick directions (choice pages), read reports, queue follow-ups |
| **Shipping / ops** | CI jobs and deploy scripts that call `aim approve` | One thumbs up per deploy gate; every decision is logged in History |
| **Running a team brief** | Mix of simulated agents and live ones | "Brief the team": a lead splits the goal; live agents receive their part over the bridge |

The point: when you run many agents at once, the bottleneck is **you**. Every agent needs a "yes",
an answer or a correction at some point. The workspace turns that into a queue you can work through
quickly, with gestures, while you see who is busy, who is blocked, and what is next.

## 1. Start the bridge

Double-click `AIManagerBridge.exe` (build it with `Bridge/build_exe.ps1`, or let GitHub build it: see
"Windows exe" below). From source: `python Bridge/aim_bridge.py`. Console only: add `--no-gui`.
The dashboard at http://127.0.0.1:8787 lets you approve requests without the headset too.

If Windows asks about the firewall, choose **Allow** (private networks). The bridge listens on your
Wi-Fi only so your headset can find it.

## 2. Connect the headset

- **Wi-Fi (default):** Settings > Agent Bridge > tap your PC > type the pairing code shown on the PC.
  Discovery is a UDP broadcast on port 8788. If your router isolates Wi-Fi clients (some guest
  networks do), your PC will not appear: use Advanced > Address instead.
- **USB (developers):** `python Bridge/aim_bridge.py --local-only`, then `adb reverse tcp:8787 tcp:8787`
  (Unity: **Connect headset (USB)**, which also runs after every build). No pairing is needed over USB.

Pairing details: the code is stored in `~/.aim/pair_code`, the token it hands out in `~/.aim/token`.
Five wrong codes lock pairing for a minute. **New code (unpair all headsets)** in the window revokes every
paired headset. Other devices on the network can do nothing without the token.

Settings shows **CONNECTED** and how many agents are live. Live agents have a teal halo and a `LIVE` tag.

### Windows exe
`powershell -ExecutionPolicy Bypass -File Bridge/build_exe.ps1` builds `Bridge/dist/AIManagerBridge.exe`
(needs python.org Python). Or push a git tag such as `bridge-v1.0`: the GitHub workflow in
`.github/workflows/bridge-exe.yml` builds the exe and attaches it to the release.

## 3. Connect agents

### Any script or terminal: the `aim` CLI

```bash
python Bridge/aim.py --agent deploy-bot approve "Deploy v2.3 to prod" --file plan.md   # exit 0 = approved
python Bridge/aim.py --agent api ask "Which DB?" --options "Postgres,SQLite"           # prints the answer
python Bridge/aim.py --agent nightly report "Tests" "312 passed, 2 flaky"               # FYI page
python Bridge/aim.py --agent api status working "Refactoring auth" --progress 0.4
python Bridge/aim.py --agent api next                                                   # waits for a task from VR
```

Exit codes from `approve`: 0 approved, 1 changes requested (the feedback is printed), 2 rejected.

### Claude Code: permission prompts go to VR (hook)

Add this to `.claude/settings.json` in the project (use your own path to `aim.py`):

```json
{
  "hooks": {
    "PermissionRequest": [
      { "hooks": [ { "type": "command", "command": "python \"D:/Unity projects/AI Manager Workspace/Bridge/aim.py\" --agent api-server hook", "timeout": 600 } ] }
    ],
    "UserPromptSubmit": [
      { "hooks": [ { "type": "command", "command": "python \"D:/Unity projects/AI Manager Workspace/Bridge/aim.py\" --agent api-server hook" } ] }
    ],
    "Stop": [
      { "hooks": [ { "type": "command", "command": "python \"D:/Unity projects/AI Manager Workspace/Bridge/aim.py\" --agent api-server hook" } ] }
    ]
  }
}
```

- Whenever Claude Code would ask "Allow this command?", the agent `api-server` walks the request to
  your desk instead. Thumbs up allows it; palm or reject denies it.
- If the bridge isn't running, or you don't decide within 9 minutes, Claude Code falls back to its
  normal prompt. It is never blocked.
- `UserPromptSubmit` marks the desk as working; `Stop` marks it idle. To get a report page with
  Claude's last message after every turn, add `--report-on-stop` to the Stop hook (this can be a lot
  of pages).

**Security:** the bridge only accepts JSON requests from this PC (it rejects requests from web pages
in your browser, and DNS-rebinding hosts). Other devices on the network must pair first (6-digit code) and then send the token.

### Claude Code / Claude Desktop / any MCP client: manager tools (MCP)

`.mcp.json` in the project:

```json
{
  "mcpServers": {
    "ai-manager": {
      "command": "python",
      "args": ["D:/Unity projects/AI Manager Workspace/Bridge/aim.py", "--agent", "research", "mcp"]
    }
  }
}
```

Tools: `request_approval`, `ask_manager`, `report_to_manager`, `set_status`, `wait_for_task`.
Tell the agent, for example: *"Get my approval before changing the schema, and report when done."*
Approvals can take a while; if your client times tools out, raise its limit (Claude Code:
`MCP_TOOL_TIMEOUT=900000`).

### Workers: tasks from VR, done by Claude Code

```bash
python Bridge/aim.py --agent api --role "Backend" worker --cwd ~/code/api
python Bridge/aim.py --agent web --role "Frontend" worker --cwd ~/code/web
```

Each worker takes a desk. Give it a task from its desk card (**Assign task**) or with
**Brief the team**. It runs `claude -p` in that folder, then brings you the result for approval.
"Request changes" continues the same Claude session with your feedback. Use `--cmd "your-tool"` to
run any other command; the task text is passed on stdin.

## HTTP API (for your own integrations)

| Method | Path | Body / query | Notes |
|---|---|---|---|
| POST | `/api/agents` | `{agent, role?, kind?}` | register / heartbeat |
| POST | `/api/status` | `{agent, state: working\|idle, title, progress?}` | desk shows it |
| POST | `/api/requests` | `{agent, kind: approval\|question\|choice\|report\|error, title, text, options?, task?}` | returns `{id}` |
| GET | `/api/requests/{id}?wait=50` | | long-poll; `decision: {action, text, approved}` |
| DELETE | `/api/requests/{id}` | | withdraw (answered elsewhere) |
| GET | `/api/tasks/next?agent=X&wait=50` | | tasks you assigned in VR |
| GET | `/api/events?since=N&wait=20` | | what the headset polls |
| POST | `/api/decisions` | `{id, action, text}` | what the headset / dashboard send |

Decision actions: `approve`, `changes`, `reject`, `answer`, `decide` (let the agent decide),
`choose`, `retry`, `ack`, `followup`.
