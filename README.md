# AI Manager Workspace

**A hands-first mixed-reality command desk for managing a team of AI agents on Meta Quest 3.**

AI agents are fast. Humans are the bottleneck. With several agents running, you become a switchboard: a permission prompt in one terminal, a plan to approve in another, a question buried in a chat. AI Manager Workspace turns that into a room. You sit at an elevated glass desk overlooking your team. Each agent is a robot at its own desk. When one needs you, it **walks up and lays its request on your desk**. You decide with your hands.

> Built for the Meta VR Start Developer Competition 2026 (Productivity track). Hands-first end to end: controllers are optional.

## Features

- **Requests come to you.** Agents walk to your desk, ride a small lift and present a page. Each request gets only the actions that fit it:
  - plan or result: Approve, Request changes, Reject
  - question: Answer (keyboard), Let the agent decide, Drop
  - choice: one button per option
  - blocker: Retry or Drop
  - report: Got it, or Follow up
- **Hand gestures with safety:** thumbs up / thumbs down / palm out with a filling ring, a hold time you can tune, and "only while looking at the page" so nothing fires by accident.
- **Brief the team:** give one goal and a lead agent splits it into roles and tasks.
- **Manager control:** tap the desk bell to hold the queue, a capped queue with arrival pacing, six layouts, up to ten agents, room themes, mixed-reality passthrough.
- **Wrist menu** on your palm: height, passthrough, back to desk, settings, hold.
- **Horizon-style windows:** move, resize from corners, snap, minimize, pinch-grab.
- **Works instantly:** demo mode simulates agents, no setup and no keys.
- **Real AI, your choice:** Claude, OpenAI, Gemini or any OpenAI-compatible endpoint. **Each agent can use a different provider.**
- **Real agents join:** the [Agent Bridge](Bridge/README.md) lets Claude Code sessions, scripts, CI jobs and MCP clients take desks. When Claude Code asks "Allow this command?", the agent walks the request to you in VR and your thumbs up goes back.
- Guided tutorial, session persistence, comfort settings for seated and standing use.

## Requirements

- Unity **6000.4.0f1**, Universal Render Pipeline
- Meta XR SDK (v209) with the Interaction SDK
- Meta Quest 3 / 3S (Android, ARM64, minimum API 32)
- Python 3.9+ for the optional bridge (standard library only)

## Build and run

1. Open the project in Unity 6000.4.0f1.
2. **Tools → AI Manager → Build Main Scene** rebuilds the scene from code (about 5–10 minutes).
3. Switch to the Android build target, then **File → Build Profiles → Build and Run** with the headset connected.
4. Release builds need your own keystore (Player Settings → Publishing Settings). Keystores are git-ignored. Never commit one.

The app starts in **demo mode**. No API key is needed.

## Use real AI providers

Open **Settings → AI** in the app, choose a default brain (Demo / Claude / OpenAI / Gemini / Custom) and enter a key, or push one from the editor with the Quest config tool. Keys are stored on the device only and are never part of the build or this repository. A per-agent **BRAIN** button on the agent card overrides the team default.

To test every provider without keys or cost: `python Bridge/mock_llm.py` runs a mock of all four APIs on port 8899.

## Connect real agents (Agent Bridge)

```
python Bridge/aim_bridge.py          # server + dashboard on :8787
adb reverse tcp:8787 tcp:8787        # lets the headset reach your PC
```

See [Bridge/README.md](Bridge/README.md) for the CLI, the Claude Code permission hook and the MCP server.

## Project layout

| Path | What |
|---|---|
| `Assets/Scripts/Agents` | agent state machine, queue, robot avatar |
| `Assets/Scripts/AI` | provider client, team planner, task dispatcher |
| `Assets/Scripts/Interaction` | hand gestures, keyboard routing, near-grab, input modes |
| `Assets/Scripts/UI` | settings, tutorial, window chrome, wrist menu |
| `Assets/Scripts/Workspace` | desk, passthrough, room themes, eye-height station |
| `Assets/Scripts/Editor` | scene builder and Quest setup tools |
| `Bridge/` | Python bridge, CLI, MCP server, mock LLM |
| `docs/` | design notes, store and submission material |

## Status

Playable on Quest 3 in demo mode and with live providers. Roadmap: voice commands, MRUK desk placement around your real room, shared co-located reviews, an analytics wall, more bridge integrations.

## Privacy

The app collects no data. If you add your own API key, your prompts are sent to that provider. See [docs/PRIVACY.md](docs/PRIVACY.md).
