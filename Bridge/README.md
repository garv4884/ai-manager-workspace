# AI Manager Bridge

Run real agents on your PC and manage them from the VR workspace. Each agent gets a desk, walks
its requests up to you, and waits for your decision: thumbs up, an answer, a chosen option, or
"request changes". You can also hand tasks to these agents from VR, one at a time or as a whole
team brief.

Everything is Python standard library: nothing to install.

```
 Claude Code ──hook/MCP──┐
 scripts / CI ──aim CLI──┼──►  aim_bridge.py (PC, :8787)  ◄──USB (adb reverse) or Wi-Fi──►  Quest app
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

```bash
python Bridge/aim_bridge.py
```

Or in Unity: **Tools > AI Manager > Quest API Key + Agent Bridge > Start bridge**.
Open http://127.0.0.1:8787: the dashboard lets you approve requests without the headset too.

## 2. Connect the headset

- **USB (simplest):** in the same Unity window, click **Connect headset (USB)**. This runs
  `adb reverse tcp:8787 tcp:8787`, and it also runs automatically after every Android build.
  Run it again after replugging the cable.
- **Wi-Fi:** start the bridge with `python Bridge/aim_bridge.py --lan`. In the headset go to
  **Settings > Bridge address** and enter your PC's IP (e.g. `192.168.1.20`). In LAN mode,
  devices other than this PC must send the token from `~/.aim/token`.

Settings shows **BRIDGE CONNECTED** and how many agents are live. Live agents have a teal halo and
a `LIVE` tag.

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
in your browser, and DNS-rebinding hosts). In `--lan` mode, every other device must send the token.

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
