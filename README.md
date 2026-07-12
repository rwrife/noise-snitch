# noise-snitch 🔊🕵️

**A tiny Windows tray app that snitches on which app just made that sound.**

That random *ding*. The phantom notification. The ad talking from a buried browser tab. noise-snitch keeps a live blotter of every sound your PC makes and names the guilty process — caller ID for your speakers.

---

## Why?

Windows shows you a *live* volume mixer, but the instant a sound stops, the evidence is gone. There's no history, no "what was THAT 8 seconds ago?" noise-snitch fills that gap: it watches per-app audio sessions, catches the moment an app *starts* making noise, and logs **who, when, and how loud** — right from your system tray.

## Status

🚧 Early. **M1–M5 done; M6 in progress.** The tray app boots (icon + Quit),
wires up NAudio to read per-app audio sessions on a timer, turns that stream into
clean **noise events** (silent → active onset detection with debounce so a
continuous stream snitches once), and renders them in a **blotter flyout**:
left-click (or double-click) the tray icon to pop a list of recent events —
newest first, with **per-app icons**, relative timestamps and **friendly app
names** (`3s ago — Google Chrome`), auto-refreshing while open, and a friendly
empty state when all is quiet. Events live in an in-memory ring buffer and are
also logged to `%LOCALAPPDATA%\noise-snitch\noise-snitch.log`.

**M5 delivered:** the tray icon now **flashes** on every new noise event — the
calm ear glyph briefly lights up (brighter amber + a glowing ring) so a glance at
the tray tells you something just made a sound; bursts coalesce into one steady
flash rather than strobing, then it settles back to calm. Plus friendly process
names in the blotter and persisted **settings** — poll interval, how many events
to keep, and the onset peak/debounce thresholds load from (and are written to)
`%LOCALAPPDATA%\noise-snitch\settings.json`, so you can tune the snitch and it
remembers. And the blotter now shows each culprit's **app icon** next to its
name — extracted from the owning process's executable and cached, with a neutral
placeholder dot when a process is a system session or has already exited — which
closes out the M5 polish checklist.

**New in M6 (so far):** optional **durable history**. Turn on `PersistLog` and
every noise event is appended to a rolling, size-capped **JSONL** log
(`%LOCALAPPDATA%
oise-snitch
oise-log.jsonl`) that survives restarts; a
**“Copy last hour”** tray action puts a tidy, paste-ready report (with a per-app
tally) on your clipboard so you can call out a repeat offender. Persistence is
**off by default** — the snitch stays local-only until you opt in. See
[PLAN.md](./PLAN.md) for the roadmap and
[issues](https://github.com/rwrife/noise-snitch/issues) for milestones.

**New (v0.2 backlog):** **Mute-the-snitched.** Right-click any blotter row to
**mute the culprit** — noise-snitch finds that app's live audio session and
silences it (EarTrumpet-lite), right from the row that snitched. Right-click
again to unmute. Muted apps are shown dimmed and struck-through with a 🔇 marker
so you can see at a glance who you've silenced. The shared **System sounds**
session isn't offered (it's not a single culprit app), and if an app has already
gone quiet there's simply nothing to mute.

**Also new (v0.2 backlog):** **Quiet-hours alerting.** Define a focus/sleep
window and noise-snitch stops letting sounds slip by unnoticed during it: any app
that makes noise inside your quiet hours gets **escalated** with a loud tray
balloon ("🔊 Google Chrome just made a sound during your quiet hours") on top of
the usual flash. The window is hand-editable `HH:mm` and **wraps past midnight**
(e.g. `22:00`–`07:00`), so overnight focus Just Works. Off until you opt in.

## Planned MVP (v0.1)

- Lives in the **system tray**, no main window.
- Detects when any app goes **silent → making sound**.
- Logs **timestamp · process name · peak level · session name**.
- A tray **flyout blotter** of recent noise events, newest first.
- Tray icon **flashes** when someone gets snitched on.

## Settings

On first launch noise-snitch writes a settings file you can hand-edit:

```
%LOCALAPPDATA%\noise-snitch\settings.json
```

| Key | Default | Meaning |
| --- | --- | --- |
| `PollIntervalMs` | `750` | How often audio sessions are sampled (ms). |
| `EventsToKeep` | `200` | Size of the in-memory blotter ring buffer. |
| `PeakThreshold` | `0.015` | Peak meter floor (`0`–`1`) an app must cross to count as "sounding". |
| `ReleaseMs` | `1000` | Debounce: continuous quiet required before the same app can snitch again (ms). |
| `PersistLog` | `false` | **M6:** when `true`, append every event to the on-disk JSONL log (durable history). Off = memory-only. |
| `MaxLogBytes` | `5242880` | **M6:** rolling size cap (bytes) for the log before the oldest lines are rotated out (default 5 MiB). |
| `QuietHoursEnabled` | `false` | **v0.2:** when `true`, escalate onsets that land inside the quiet window with a loud tray toast. |
| `QuietHoursStart` | `"22:00"` | **v0.2:** inclusive quiet-window start, local wall-clock `HH:mm` (24-hour). |
| `QuietHoursEnd` | `"07:00"` | **v0.2:** exclusive quiet-window end. If earlier than start, the window wraps past midnight. |
| `IgnoredApps` | `[]` | **v0.2:** process names to ignore. Onsets from these apps are dropped from the feed and blotter. Matched case-insensitively with a trailing `.exe` stripped, so `"Spotify"`, `"spotify"`, and `"spotify.exe"` all mean the same app. |

Values are range-checked on load — a missing, empty, corrupt, or out-of-range
file safely falls back to the defaults, so the app always starts. Changes take
effect on the next launch.

## Durable history & export (M6)

By default noise-snitch keeps history only in memory for the current session.
Set `"PersistLog": true` in `settings.json` to also write a durable log:

```
%LOCALAPPDATA%\noise-snitch\noise-log.jsonl
```

The file is **JSONL** — one JSON object per line, appended as events happen, so
it's trivially greppable and a crash costs at most the last partial line. When it
grows past `MaxLogBytes` the oldest lines are dropped and the newer tail is kept
(rotation-in-place), so it self-limits with no cron or cleanup.

**Copy last hour:** right-click the tray icon → **Copy last hour** (shown only
when persistence is on) to drop a paste-ready report on your clipboard, e.g.:

```
noise-snitch — 3 events (last hour) as of 14:05:12

  14:05:07  Google Chrome   peak 0.42
  14:03:55  Slack           peak 0.31
  13:58:20  System sounds   peak 0.88

Top offenders: Google Chrome ×1, Slack ×1, System sounds ×1
```

### Data format

Each line is one event (`schema v1`); keys are kept short to keep the file small:

| Key | Type | Meaning |
| --- | --- | --- |
| `t` | string | ISO-8601 (round-trip) **UTC** timestamp of the onset. |
| `pid` | number | Owning process id (`0` = the Windows *system sounds* session). |
| `name` | string | Resolved process name (e.g. `chrome`). |
| `peak` | number | Peak meter value (`0`–`1`) at the moment of onset. |
| `session` | string | Windows session display name, when present. |

Example line:

```json
{"t":"2026-07-02T14:05:07.123Z","pid":4821,"name":"chrome","peak":0.42,"session":"Chrome"}
```

Unparseable lines (a hand-edit typo or a torn final line) are skipped on read,
not fatal.

## Quiet-hours alerting (v0.2)

A live flash is easy to miss when you're heads-down or asleep. **Quiet hours**
turn any noise made inside a window you choose into a hard-to-miss **tray
balloon** — so you find out immediately that something piped up during your focus
time.

Enable it in `settings.json`:

```json
{
  "QuietHoursEnabled": true,
  "QuietHoursStart": "22:00",
  "QuietHoursEnd": "07:00"
}
```

- Times are **local wall-clock** `HH:mm` (24-hour). Sloppy input like `9:5` is
  accepted and canonicalized to `09:05`; anything unparseable falls back to the
  default rather than silently disabling the feature.
- The window is **`[start, end)`** — start inclusive, end exclusive.
- When **end is earlier than start** the window **wraps past midnight**, so
  `22:00`–`07:00` covers the whole night. (Want almost-all-day? Use distinct
  endpoints like `00:00`–`23:59`; identical endpoints mean "no window".)
- During quiet hours each onset still flashes the tray icon **and** raises a
  balloon naming the culprit. Outside the window, behaviour is unchanged.

Escalation is **off by default** and, like every setting, takes effect on the
next launch.

## Per-app ignore list (v0.2)

Some apps you simply don't care about — your music player, a game — and you'd
rather they never snitch. Add their process names to `IgnoredApps` in
`settings.json`:

```json
{
  "IgnoredApps": ["spotify", "vlc"]
}
```

- Onsets from an ignored app are **dropped before** they reach the blotter, the
  flash, and the durable log — as if the app made no sound.
- Matching is **case-insensitive** and a trailing `.exe` is stripped, so
  `"Spotify"`, `"spotify"`, and `"spotify.exe"` all silence the same app.
- Duplicates and blanks are cleaned up on load; the persisted list is
  canonicalized (lower-cased, sorted).

> A settings UI to add/remove ignored apps and an in-blotter "ignore this app"
> action are the next slices; the filtering engine and file format land here.


## Noise leaderboard (v0.2)

The blotter answers *"what just made that sound?"* one event at a time. The
**leaderboard** answers *"who keeps doing this?"* — right-click the tray icon →
**Leaderboard** for an aggregate view of today's noisiest apps, ranked by how
many noise events each produced:

```
1. 🥇 Slack — 38
2. 🥈 Google Chrome — 22
3. 🥉 Zoom — 9
4. Firefox — 4
```

- **Window:** today (local calendar day) by default.
- **Ordering:** event count descending, then app name ascending for ties —
  fully deterministic.
- **Grouping:** counts are per app, not per pid: `chrome`, `chrome.exe`, and
  `Chrome` all fold into one row, and the shared **System sounds** session is
  its own bucket. Names are shown via the same friendly-name mapping as the
  blotter, with 🥇🥈🥉 medals for the top three.
- When nothing's made a peep yet, you get a friendly empty state instead of an
  empty box.

Aggregation and rendering are pure and WinForms-free (`Leaderboard`,
`LeaderboardFormatter`), so ranking, tie-breaking, and the empty state are all
unit-tested without any live audio.


## Stack

C# / .NET 8 · [NAudio](https://github.com/naudio/NAudio) (WASAPI / Core Audio sessions) · WinForms `NotifyIcon`. Boring, fast, Windows-native — ships as a single `.exe`.

## Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) on **Windows**
(it's a WinForms tray app — `net8.0-windows`).

```sh
# build everything
dotnet build noise-snitch.sln -c Release

# run the unit tests (edge-detection + ring buffer)
dotnet test noise-snitch.sln -c Release

# run the tray app (look for the icon in your system tray)
dotnet run --project src
```

### Single-file exe

```sh
dotnet publish src/NoiseSnitch.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish
# -> publish/noise-snitch.exe
```

CI builds, **runs the tests**, and uploads the `noise-snitch.exe` artifact on
every push/PR (see [`.github/workflows/build.yml`](./.github/workflows/build.yml)).

## License

MIT (see [LICENSE](./LICENSE)).

---

*Part of an automated tool-lab experiment. Topic: `auto-tool-lab`.*
