# noise-snitch 🔊🕵️

> "What just made that sound?!" — answered, finally.

## 1. Pitch

`noise-snitch` is a tiny Windows system-tray app that keeps a live blotter of **every sound your PC makes** and names the app responsible. That random *ding*, the phantom notification chime, the ad that started talking from a buried browser tab — noise-snitch catches the culprit, timestamps it, and shames it in a scrollable feed. It's the audio equivalent of caller ID for your speakers.

## 2. Trend inspiration

What I saw on the web (June 2026) that pointed here:

- **Windows still leaves obvious gaps in notification control** — windowsforum's *Best Windows 11 Utility Apps (2026)* explicitly calls notification/notification-noise management one of the friction points Microsoft hasn't solved, fueling "a new generation of lightweight utilities." → https://windowsforum.com/threads/best-windows-11-utility-apps-2026-fix-transfer-launchers-menus-focus.414254/
- **The "focus / dumb-phone / Simple Zen" wave** on Product Hunt (Simple Zen launched June 26, 2026) shows strong demand for *taking back control of interruptions*. noise-snitch is the diagnostic half of that fight. → https://www.producthunt.com/categories/productivity
- **Lightweight tray utilities with personality are having a moment** (Taskbar Buddy, the desktop-pet/tray-toy explosion on itch.io). I'm borrowing the *personality + tray-native* vibe but pointing it at a genuine utility instead of a cosmetic toy. → https://itch.io/tools/tag-desktop-pet
- Windows already exposes the data (per-app audio sessions in the Volume Mixer) but **shows no history** — there's a gap between "live mixer" and "what happened 8 seconds ago."

## 3. Why it's different

- **Volume Mixer / Windows Sound settings** show *current* sessions and levels, but vanish the instant a sound stops. noise-snitch is a **time machine**: it logs the moment, the app, and how loud, so you can answer "what was THAT?" *after* it already happened.
- **EarTrumpet** (the beloved tray mixer) is about *controlling* per-app volume right now. noise-snitch is about *forensics & history* — a different job. They'd happily coexist.
- **Desktop pets / Taskbar Buddy** are cosmetic toys. noise-snitch has personality but earns its tray slot by solving a real "wait, what made that noise" annoyance.
- **Notification managers** (Windows Focus Assist, etc.) suppress *visual* toasts. Nobody is building a forensic log of *audible* events tied to the responsible process. That's the open lane.

As far as I know, there's no small, free, tray-native "audio event blotter with app attribution" — that's the bet.

## 4. MVP scope (v0.1)

The smallest useful thing:

- Runs in the **system tray**, no main window required.
- Polls the Windows Core Audio session API on a short interval and detects when a session goes from **silent → active** (i.e., an app started making sound).
- Captures per event: **timestamp**, **process name** (e.g., `chrome.exe` / friendly name), **peak level**, and **session display name** if available.
- A **flyout/popup list** (open from the tray icon) showing the last N audio events, newest first — the "blotter."
- **Tray icon flashes** briefly when a new noise is snitched, so you can glance and see who did it.
- Events kept in memory (ring buffer) for the session; no persistence required yet.

That's it. One screen, one job: *who just made noise.*

## 5. Tech stack

Boring, fast, Windows-native:

- **Language: C# / .NET 8** — first-class access to Windows audio APIs, trivial tray apps, single-file `dotnet publish`, fast to ship.
- **Audio: NAudio** (`WasapiLoopbackCapture` / `MMDeviceEnumerator` + `AudioSessionManager`) — the standard, well-documented wrapper over Windows Core Audio (WASAPI). No reinventing COM interop.
- **UI: WinForms `NotifyIcon` + a lightweight popup** — the lowest-ceremony path to a tray app with a flyout list. (WPF is a possible later swap; WinForms is faster for v0.1.)
- **Process attribution:** `AudioSessionControl.GetProcessID()` → `Process.GetProcessById()` for the name/icon.
- **Build/CI:** GitHub Actions on `windows-latest`, `dotnet publish -c Release` self-contained exe artifact.

Justification: this is a Windows-only audio-introspection tool; .NET + NAudio is the path of least resistance and ships a single small `.exe` users can just run.

## 6. Architecture

```
noise-snitch/
├─ src/
│  ├─ AudioWatcher/      # NAudio session enumeration + silent→active edge detection
│  ├─ Model/             # NoiseEvent record (time, pid, processName, peak, sessionName)
│  ├─ Tray/              # NotifyIcon, flash-on-event, context menu
│  └─ Ui/                # Blotter flyout (list of recent NoiseEvents)
└─ tests/                # edge-detection unit tests over fake session snapshots
```

Key modules:

- **AudioWatcher** — the brain. Enumerates audio sessions on a timer, diffs peak-meter state vs. last tick, emits a `NoiseEvent` on each silent→active transition (with debounce so a continuous stream doesn't spam).
- **EventStore** — in-memory ring buffer of recent events.
- **Tray** — owns the `NotifyIcon`, subscribes to `NoiseEvent`s, flashes the icon, exposes the blotter + Quit.
- **Ui (Blotter)** — renders the recent-events list; click an event later to jump to the app (future).

## 7. Milestones (each shippable)

1. **M1 — Scaffold + hello-world.** .NET 8 solution, tray app that boots, shows a `NotifyIcon` with a "noise-snitch is watching 👀" tooltip and a Quit menu. GitHub Actions builds a Windows exe. *(Ships: a tray icon that runs.)*
2. **M2 — Audio session enumeration.** Wire up NAudio; on a timer, list all current audio sessions with process name + peak level. Dump to a debug log to prove the data flows. *(Ships: proof we can see per-app audio.)*
3. **M3 — Silent→active edge detection + NoiseEvent.** Diff peaks across ticks, debounce, and emit a clean `NoiseEvent` each time an app *starts* making sound. *(Ships: accurate "app X just made noise" events in the log.)*
4. **M4 — The blotter UI.** Tray flyout listing the last N `NoiseEvent`s, newest first, with relative timestamps ("3s ago — chrome.exe"). *(Ships: you can SEE the history.)*
5. **M5 — Flash + polish.** Tray icon flashes/badges on each new event; friendly process names + app icons; debounce tuning; settings for poll interval & how many events to keep. *(Ships: it feels good to use.)*
6. **M6 — Persistence + export.** Optional rolling on-disk log (SQLite or JSONL) and "copy/export last hour" so users can report a repeat offender. *(Ships: forensics that survive restarts.)*

## 8. Backlog / future features (v0.2+)

1. **Mute-the-snitched** — one click from a blotter entry to mute that app's session (EarTrumpet-lite).
2. **"Who keeps doing this?"** leaderboard — ranks apps by number of noise events per day.
3. **Quiet-hours alerting** — if anything makes noise during your set focus window, flag it loudly (or to a notification).
4. **Per-app rules** — allowlist apps you don't care about (e.g., your music player) so they never snitch.
5. **Sound fingerprint thumbnail** — capture a 1–2s waveform/clip of the offending audio so you can recognize *which* ding it was.
6. **Browser-tab drill-down** — best-effort: when the culprit is a browser, surface the active/most-recent tab title.
7. **Daily digest** — "Today your PC made 214 sounds; Slack was responsible for 38%."
8. **Global hotkey** — pop the blotter instantly without reaching for the tray.
9. **Notification-only mode** — toast "🔊 chrome.exe just made a sound" instead of (or with) the flash.
10. **Theming / personality packs** — swap the snitch's tone ("polite butler" vs. "tattletale gremlin") in tooltips and digests.
11. **Cross-session timeline view** — a real scrollable, filterable window for power users.
12. **Loopback-level meter** — show overall system output level alongside per-app events.

## 9. Out of scope (deliberately NOT building)

- **macOS / Linux ports.** v0.x is Windows-only — that's where the audio-session attribution is cleanest and the friction is documented.
- **A full mixer / EQ / per-app volume control suite.** We log and (later) maybe mute; we are not rebuilding EarTrumpet or the Windows Sound control panel.
- **Recording/saving full audio streams.** No always-on capture-to-disk of your audio (privacy + scope). The optional waveform thumbnail (backlog) is a tiny, opt-in clip at most.
- **Cloud sync / accounts / telemetry.** Local-only. The snitch tells *you*, nobody else.
- **Mobile.** Not a phone app.
