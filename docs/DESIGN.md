# AI Manager Workspace: product and system design

## 1. The idea in one line

**A mixed-reality command desk for people who run many AI agents at once.** Agents work in parallel at
their own desks; whenever one needs a human (a plan to approve, a question, a choice, a blocker, a
finished result) it walks that request to your desk as a page. You decide with a glance and a hand
gesture, and the team keeps moving.

## 2. Why it exists

Agents are fast; people are the bottleneck. With five agents running, the human becomes a switchboard:
terminal tabs, permission prompts, half-read summaries and lost context. Work stalls because nobody
noticed that an agent was waiting.

The workspace turns that mess into a **single, physical, prioritised queue**:

| Problem with many agents | What the workspace does |
|---|---|
| Requests are scattered across windows | One line of agents walking to one desk, one page at a time |
| You can't see who is idle, busy or blocked | A status tag over every agent, a glowing desk strip, and a team board on the wall |
| Every decision needs typing | Thumbs up / down / palm, or option buttons generated per request; type only when you must |
| Context switches break focus | The page carries the context (who, what, revision, options) |
| You want to stop the flow and think | Hold the queue; agents keep working and line up quietly |
| Agents forget what the team did | Shared team memory (recent approved work) goes into every live prompt |

## 3. Who uses it, and for what

1. **Developers** running several Claude Code sessions (one per repo or feature). The bridge routes
   their permission prompts and plans to the desk; workers run tasks assigned from VR.
2. **Researchers and analysts**: agents gather sources and bring findings and direction choices.
3. **Team leads and founders**: brief the team with one goal; a lead agent splits it into roles and
   tasks, and you approve the plan, then each deliverable, then the wrap-up.
4. **Pitch and demo mode**: a fully simulated team with no setup and no API key.

## 4. Core loop

```
 assign / brief ─► agent works (desk: typing, progress) ─► needs you ─► walks to the queue ─► lift ─► page on your desk
        ▲                                                                                            │
        └──── rework (changes, answer, choice, retry) ◄─── your decision (gesture / button / keyboard) ┘
                                                     └──► done: filed in the tray, kept in History
```

Every request has a **kind** that decides which actions you see. You never get an "Approve" button
for a question.

| Kind | Actions |
|---|---|
| Approval (plan or result) | Approve · Request changes · Reject |
| Question | Answer (keyboard) · Let agent decide · Drop |
| Choice | One button per option · Other… · Drop |
| Blocked | Retry · Drop |
| Report | Got it · Follow up |

## 5. Spaces and information architecture

| Space | Purpose | Always reachable? |
|---|---|---|
| **Desk console** (front) | The current request and its actions, gesture progress | At the desk |
| **Page** (floats above the desk) | The request content | While someone presents |
| **Workspace panel** (left wing) | Brief the team, layouts, add agent, discussion, hold queue, demo, MR, settings | At the desk |
| **Agent card** (right wing) | Selected agent: status, task, assign, role, review next, remove | At the desk |
| **Desk keyboard** (desk surface) | Answers, feedback, tasks; Meta keyboard and voice one tap away | When text is needed |
| **Wrist menu** (left palm) | Brief, demo, layout, MR, hold, back to desk, fit desk | Anywhere |
| **Team board** (far wall) | Everyone's status at a glance, goal progress, bridge status | From anywhere in the room |
| **Settings window** (above the desk) | Everything configurable, grouped in tabs | From the workspace panel or the wrist menu |
| **Done tray** | Pinch it to open History | At the desk |

## 6. Interaction model (hands first)

- **Default input is hands.** Controllers are blocked in hands mode: their rays and models are off.
  If you pick them up, a notice explains how to switch (Settings › Hands & Input, or hold both triggers
  for 2 seconds).
- **Point and pinch** for anything far away (agents, desks, the tray). **Poke** for panels within reach.
- **Gestures** are shortcuts for the current request, held briefly to avoid accidents:
  thumbs up (primary), thumbs down (changes), palm out (reject / close the keyboard). A ring at your
  hand and a bar on the console show the hold.
- **Wrist menu** opens on the non-dominant palm and accepts only the other hand.
- **Movement**: walk, or use Meta hand teleport (L-shape, then pinch). "Back to desk" returns you home.

## 7. System architecture

```
                        ┌──────────────── GameManager (scene hub) ─────────────────┐
 Input                  │ Agents  Queue  AI  Dispatcher  Planner  Layout  Keyboard │
  HandGestureRecognizer ┤ Station  Session  Bridge  Settings  Input  Tutorial      │
  ISDK ray / poke       └───────┬─────────────┬───────────────┬────────────────────┘
  TextInputService              │             │               │
                         AgentController ◄─ QueueManager ─► ReviewDesk (anchor, tray, sounds)
                          (task FSM,           (line, lift,
                           movement, face)      presenter)
                                │
            ┌───────────────────┼─────────────────────────┐
     AIServiceManager     TeamPlanner               AgentBridgeClient ──HTTP──► aim_bridge.py ◄── Claude Code hook / MCP / CLI / workers
   (offline sim or        (brief → plan →            (links real agents            (PC, :8787, dashboard)
    Claude API,            tasks → wrap-up)           to desks)
    team memory)
                                │
     SessionStore (session.json: roster, desks, tasks, briefs, history)   AppSettings (PlayerPrefs: per-device preferences)
```

### Agent state

- `AgentStatus` is what the agent needs from you (FREE, WORKING, NEEDS APPROVAL, NEEDS INPUT,
  BLOCKED, HAS UPDATE). It drives the tag, the desk strip, the eyes and the antenna.
- `AgentActivity` is where the agent is (at desk, walking, in line, presenting, in a meeting). It is
  kept separate so that walking never hides "needs you".
- Task sources: the demo backlog, tasks you type, team briefs, and the bridge (external requests and
  tasks sent from VR).

### Persistence

| Store | Contents | Where |
|---|---|---|
| `session.json` | Team roster, desk poses, layout, queued and waiting work, briefs, history, demo/hold | `persistentDataPath` |
| `config.json` | API key, model, bridge URL and token (never in git or the APK) | Quest: `persistentDataPath`; Editor: `UserSettings/` |
| `AppSettings` | Input mode, gesture timing, menu hand, room theme, MR, audio, tutorial done | PlayerPrefs |

## 8. Onboarding

The tutorial runs on first launch (skippable at any step, replayable from Settings). It has 12 steps
in five chapters:

1. **Welcome**: what the app is, and why.
2. **Your hands**: point and pinch, poke, the wrist menu, review gestures.
3. **Your team**: status tags and the board; a guided approval; a guided question with the keyboard.
4. **Your tools**: select and assign, the workspace panel, moving around.
5. **Make it yours**: settings, real agents, then start with the demo team or an empty team.

Steps that ask you to *do* something detect it (a pinch, the palm menu opening, a gesture, a decision)
and tick themselves off; Next is always available, so nobody gets stuck.

## 9. Settings map

| Tab | Settings |
|---|---|
| General | Demo mode, hold queue, auto-open keyboard for questions, replay tutorial, reset session |
| Hands & Input | Hands / Controller mode, gesture shortcuts on/off, gesture hold (quick / normal / careful), wrist menu hand |
| AI | Mode, enter / test / remove API key, simulated work speed |
| Agent Bridge | Status, live agents, bridge address, how to connect |
| Room | Theme (Daylight Studio, Golden Hour, Night Loft, Nordic Calm), mixed reality |
| Sound | Volume, UI clicks, agent chimes |
| History | Every finished request (approved, answered, dropped) |
| About | What the app is, the gestures cheat sheet, version |

## 10. Quality bar

- Nothing blocks: every wait has a way out (skip, palm closes, hold the queue, bridge timeouts fall
  back to normal prompts).
- Every action gets feedback: sound, a visual state, and the gesture ring.
- Text stays readable: world-space UI at 1 mm per unit, panels angled at the user, tags that grow with
  distance.
- Comfort: seated-height desk fit, no forced locomotion, teleport only on request.
- Privacy and security: keys never ship in the build; the bridge is localhost-only unless you turn on
  LAN mode, which needs a token.

## 11. Next steps (roadmap)

- Voice commands ("approve", "next") through the Meta Voice SDK.
- Shared anchors so a team of managers can review together in co-location.
- Spatial scene understanding: place agent desks around your real room in MR.
- An analytics wall: throughput, time-to-decision, and the agents that block most often.
