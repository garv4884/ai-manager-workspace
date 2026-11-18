# Meta VR Start Developer Competition 2026: submission kit

> **Deadline: Nov 18, 2026, 12:00 PM PT (Thu Nov 19, 1:30 AM IST).** Source: the Devpost dates page
> (start-developer-competition-26.devpost.com/details/dates). Double-check it on Devpost before you rely on it.

## Step by step (sources: Devpost overview, rules and FAQ; Meta release-channel docs)

### A. Eligibility (do first: approvals can take up to 24 h)
1. **Meta VR Start Program.** Apply from the Devpost overview page (about 5 minutes). To check you're in,
   look for the "Start Member" flair next to your name on the Meta developer forum. If it isn't there
   after 24 hours, file a ticket at developers.meta.com/vr/support.
2. **Developer access** on your Meta account (Meta Horizon Developer Dashboard).
3. On Devpost, click **Join hackathon** (log in or sign up).
4. Check the rules: you're 18+, India is eligible, one entry per person.

### B. Build a signable release APK (Unity)
5. Player Settings > Android > **Publishing Settings > Keystore Manager > Create new**. Keep the
   keystore file and passwords safe, because every future update needs them. The project currently has
   no custom keystore; Unity's debug-signed APKs are rejected by the dashboard.
6. Untick **Development Build**. Check: IL2CPP, ARM64, minimum API 32, target API 34,
   package `com.garvchopra.aimanagerworkspace`, Bundle Version Code **1** (raise it for every re-upload).
7. Leave the AI in **demo mode**, with no keys in the build, so judges can play with no setup.
8. **Build** (not Build and Run) to an `.apk`. Install it on a Quest 3 and play the whole experience with
   **hands only**.

### C. Upload to the Meta dashboard
9. developers.meta.com/horizon/manage > **Create a New App** > name "AI Manager Workspace" >
   platform **Meta Horizon Store** > Create. The App ID is in the page URL.
10. **App Metadata**: a short description and a header image. Both appear on the invite page judges open.
11. **Distribution > Release Channels > Create Release Channel**, named exactly **`Competition`**. The name
    can't be changed afterwards.
12. Upload the APK to the Competition channel. Either:
    - use **Meta Quest Developer Hub** > App Distribution > upload to a release channel, or
    - use the command line:
      ```
      ovr-platform-util upload-quest-build --app-id <APP_ID> --token <TOKEN> --apk <path.apk> --channel Competition --age-group TEENS_AND_ADULTS --notes "Competition build"
      ```
      The token is under Account > Account settings > Get Oculus platform utility token.
13. In the Competition channel: **Channel Settings > Channel Invite > Grant access to users by URL >
    Automatic approval > Save**, then copy the URL. The page shuts off 90 days after the last upload,
    which is after winners are announced, so it's fine.
14. Open the invite URL on your phone or PC as a test, then click **Install on headset**.

### D. Video (required)
15. Record **under 3 minutes** of real footage from the headset (casting or the headset's own recording);
    the XR Simulator is also allowed. Don't rely on AI-generated video. Lead with your best moment.
16. Upload to **YouTube or Vimeo** as **Public** (not Unlisted, to be safe), with English text or subtitles.

### E. Devpost form (before 12:00 PM PT)
17. Submission name, tagline (140 characters or fewer), track **Productivity**, division **New Experience**,
    the **release channel invite URL**, the video link, a description of about 500 words (inspiration,
    how you built it, future plans), a **target launch date**, the optional hand-interactions note,
    and team details.
18. Submit. You can edit the form and update the build **until the deadline**. After it, don't change
    anything, and keep the channel live until winners are announced (about Dec 11).

### Judging recap
- **Stage 1 (pass/fail):** fits the theme and track, is hands-first, and uses the platform properly.
- **Stage 2 (25% each):** Innovation & Creativity, Experience Design, Technical Implementation,
  Polish & Presentation.
- **High scores need:** 60+ fps on a Quest 3 / 3S, no bugs, and good use of gaze, hand tracking,
  passthrough, anchors and field-of-view-aware UI.
- **Design guidance:** seated-friendly, a fast start, something satisfying within 10 minutes, and not a
  thin wrapper around an AI service.

## Name and tagline

**AI Manager Workspace**

> Your mixed-reality command desk for a team of AI agents: they bring you plans, questions and results;
> you decide with a gesture. (132 characters)

## Description (about 500 words)

**Inspiration.** AI agents are fast; we are the bottleneck. With five agents running, a developer turns
into a switchboard: permission prompts in one terminal, a plan to approve in another, a question
buried in a chat. Work stalls because nobody noticed that an agent was waiting. We wanted the
calm overview of a real team lead: everyone visible, one queue, quick decisions.

**What it does.** AI Manager Workspace puts you at an elevated desk in a bright studio (or in your own
room with passthrough). Each AI agent is a friendly robot at its own desk, and its screen face, antenna
light and floating tag show whether it is free, working, or needs you. When an agent needs a human it
walks up to your desk, rides a small lift, and lays its request on the desk as a page. Every request
type gets only the actions that fit it:

- a plan or result: Approve, Request changes, Reject
- an open question: Answer (keyboard), Let the agent decide, Drop
- a choice: one button per option
- a blocker: Retry or Drop
- a report: Got it, or Follow up

You decide with **hand gestures** (thumbs up, thumbs down, palm out, with a ring filling at your hand),
desk buttons, or the desk keyboard; the Meta keyboard with voice dictation is one tap away.

**Brief the team** with one goal and a lead agent splits it into roles and tasks. Switch between six
layouts (classroom, circle, U-shape, pods, coworking, war room), add up to ten agents, slide desks with
a pinch, send free agents to a glass discussion room, or **hold the queue** while you think. A wrist
menu on your palm works anywhere in the room. History, room themes, sound, and hands/controller
settings live in a full settings window. A first-run tutorial teaches hands, gestures and the review
loop step by step.

The app is complete offline: simulated agents produce realistic plans, questions and choices. Add a key for
Claude, OpenAI, Gemini or any OpenAI-compatible endpoint (Groq, OpenRouter, Ollama) and agents think for real, with
shared team memory; each agent can even run on a different provider. Connect the open **Agent Bridge**
on your PC and real agents (Claude Code sessions, scripts, CI jobs, any MCP client) take desks in VR:
when Claude Code asks "Allow this command?", that agent walks the request to you, and your thumbs up
goes straight back.

**How we built it.**
- **Engine:** Unity 6 (URP) with the Meta XR SDK.
- **Meta building blocks and Interaction SDK:** hands-first camera rig, ray, poke, NavMesh teleport and passthrough.
- **Our own systems:** a hand-pose gesture recogniser built on ISDK joint data, NavMesh agents, and a state machine that keeps what an agent *needs* separate from *where it is*.
- **Bridge:** a zero-dependency Python service providing an HTTP API, a Claude Code permission hook, an MCP server, and a browser dashboard.

**Challenges.**
- Making gestures reliable without accidents: hold times, cooldowns, and keyboard-aware routing.
- Keeping hands primary: in hands mode controllers are fully disabled, and a gentle notice explains how to switch.
- Designing request-specific actions, so you never get an Approve button for a question.

**What's next.**
- Voice commands
- Co-located reviews with shared anchors
- MRUK placement of agent desks around your real room
- An analytics wall showing decision time and the agents that block most often

## Hand interactions note (optional field)

Fully hand-driven: point + pinch (agents, desks, tray), poke (all desk panels, keyboard, tutorial),
left-palm wrist menu (right hand only), thumbs up / thumbs down / palm-out review gestures with a
hold ring, L-shape + pinch teleport. Controllers are off by default (optional mode in Settings).

## Demo video script (2:45)

| Time | Shot |
|---|---|
| 0:00–0:15 | Hook: "Five AI agents. One of you." Wide shot of the studio, agents typing, the board on the wall. |
| 0:15–0:40 | An agent's eyes widen and it chimes, walks to the line and rides the lift; the page lands on the desk. Thumbs up, the ring fills, the page is filed in the tray. |
| 0:40–1:05 | A question: the keyboard appears, you type an answer, thumbs up sends it. A choice: tap an option. |
| 1:05–1:30 | Brief the team: one goal becomes a plan; you approve and roles appear over every head. |
| 1:30–1:50 | Layouts morph (circle to war room), add an agent, the wrist menu, hold the queue. |
| 1:50–2:20 | Real agents: Claude Code on the PC asks for permission; in VR its agent walks up; thumbs up; the command runs on the PC. |
| 2:20–2:35 | Mixed reality: the room fades to passthrough, agents in your space. Settings: room themes. |
| 2:35–2:45 | Close on the logo and tagline: "Built for hands. Built for teams of agents." |
