# Meta Horizon Store launch checklist: AI Manager Workspace

Dashboard: developers.meta.com/horizon/manage > your app > **Distribution > App submissions > v1**.
Meta's requirements change, so the dashboard's own wording wins over this list.

## 1. Build (do first)
- [ ] Player Settings > Android: **ARM64 only** (untick ARMv7), IL2CPP, min API 32, target 34
- [ ] **Bundle Version Code 2** (must be higher than every uploaded build)
- [ ] Release keystore selected (Publishing Settings), Development Build **off**
- [ ] Demo mode is the default, no keys in the build
- [ ] Profile on a Quest 3 with OVR Metrics: steady 72 fps in the busiest layout (10 agents, war room)
- [ ] Test: Meta button and back, sleep/wake (height fix), recenter, passthrough toggle, fresh install tutorial, clear-data first launch
- [ ] Run the **VRC validator** in Meta Quest Developer Hub against the APK and fix what it reports
- [ ] Re-justify cleartext HTTP: it exists only for the optional PC bridge (LAN). Consider restricting it before the store review

## 2. App metadata tabs
- [ ] **Name:** AI Manager Workspace
- [ ] **Short / long description:** see the dashboard text already used; mention demo mode and hands-first
- [ ] **Categorization:** Productivity
- [ ] **Specs:** Quest 3 and 3S (add 2 / Pro only after testing); input: hand tracking, controllers optional; play modes: seated and standing; internet: not required; multiplayer: none
- [ ] **Assets:** icon, cover art (landscape, square, portrait, per dashboard sizes), hero image, **5+ screenshots**, **trailer** (cut from the demo video)
- [ ] **Content rating:** IARC questionnaire (no violence, no purchases, no user content; mention it can call third-party AI services if the user adds a key)

## 3. Legal and data
- [ ] **Privacy policy URL:** publish docs/PRIVACY.md (see "Host the privacy policy" below), add a real contact email first
- [ ] **Data Use Checkup:** answer that the app collects no user data; optional AI providers are user-supplied
- [ ] **Pricing:** Free (a paid app needs a payout account and tax forms first)
- [ ] **Store availability:** countries

## 4. Submit
- [ ] Pick the build, paste the reviewer notes below, submit
- [ ] Review takes roughly 1-4 weeks; a first-round fix request is normal. Fix, bump the Version Code, resubmit
- [ ] Optional beta first: share the Competition release channel link publicly for feedback

## Host the privacy policy (free)
1. GitHub repo > Settings > Pages > deploy from branch `main`, folder `/docs`.
2. The policy appears at `https://garv4884.github.io/ai-manager-workspace/PRIVACY` (GitHub renders Markdown). Use that URL in the dashboard.

## Reviewer notes (paste into the submission)
```
AI Manager Workspace is a hands-first productivity app: the user manages a team of AI agents (robots at desks) who walk requests to the user's desk to approve, answer or choose.

HOW TO TEST (no account or API key needed): the app starts in Demo mode with simulated agents. A first-run tutorial (skippable) teaches hand gestures. Agents arrive at the desk every few seconds; approve with thumbs up or the desk buttons.

INPUT: Hand tracking by default; controllers are optional (Settings > Hands). Poke the desk panels, point+pinch to select, left-palm wrist menu, thumbs up/down/palm-out review gestures with a hold ring.

OPTIONAL FEATURES (off by default, not needed for review):
- Live AI: the user can add their own API key (Settings > AI) for Anthropic, OpenAI, Google Gemini or an OpenAI-compatible endpoint. Keys stay on the device. Prompts go only to the provider the user chooses.
- Agent Bridge: an optional Python service on the user's own PC connects real agents (for example Claude Code permission prompts) over the local network or USB. This is why cleartext HTTP to a LAN address is allowed. The app makes no network calls unless the user configures one of these.

PASSTHROUGH: toggled from the wrist menu (MR). The app collects no camera or tracking data.
```
