# noise-snitch 🔊🕵️

**A tiny Windows tray app that snitches on which app just made that sound.**

That random *ding*. The phantom notification. The ad talking from a buried browser tab. noise-snitch keeps a live blotter of every sound your PC makes and names the guilty process — caller ID for your speakers.

---

## Why?

Windows shows you a *live* volume mixer, but the instant a sound stops, the evidence is gone. There's no history, no "what was THAT 8 seconds ago?" noise-snitch fills that gap: it watches per-app audio sessions, catches the moment an app *starts* making noise, and logs **who, when, and how loud** — right from your system tray.

## Status

🚧 Early. **M1–M4 done; M5 in progress.** The tray app boots (icon + Quit),
wires up NAudio to read per-app audio sessions on a timer, turns that stream into
clean **noise events** (silent → active onset detection with debounce so a
continuous stream snitches once), and renders them in a **blotter flyout**:
left-click (or double-click) the tray icon to pop a list of recent events —
newest first, with relative timestamps and **friendly app names**
(`3s ago — Google Chrome`), auto-refreshing while open, and a friendly empty
state when all is quiet. Events live in an in-memory ring buffer and are also
logged to `%LOCALAPPDATA%
oise-snitch
oise-snitch.log`.

**New in M5 (so far):** friendly process names in the blotter, plus persisted
**settings** — poll interval, how many events to keep, and the onset
peak/debounce thresholds now load from (and are written to)
`%LOCALAPPDATA%
oise-snitch\settings.json`, so you can tune the snitch and it
remembers. Still to come this milestone: tray-icon flash/badge on new events and
app icons. See [PLAN.md](./PLAN.md) for the roadmap and
[issues](https://github.com/rwrife/noise-snitch/issues) for milestones.

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

Values are range-checked on load — a missing, empty, corrupt, or out-of-range
file safely falls back to the defaults, so the app always starts. Changes take
effect on the next launch.

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
