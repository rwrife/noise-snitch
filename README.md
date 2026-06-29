# noise-snitch 🔊🕵️

**A tiny Windows tray app that snitches on which app just made that sound.**

That random *ding*. The phantom notification. The ad talking from a buried browser tab. noise-snitch keeps a live blotter of every sound your PC makes and names the guilty process — caller ID for your speakers.

---

## Why?

Windows shows you a *live* volume mixer, but the instant a sound stops, the evidence is gone. There's no history, no "what was THAT 8 seconds ago?" noise-snitch fills that gap: it watches per-app audio sessions, catches the moment an app *starts* making noise, and logs **who, when, and how loud** — right from your system tray.

## Status

🚧 Early. **M1 + M2 + M3 done** — the tray app boots (icon + Quit), wires up
NAudio to read per-app audio sessions on a timer, and now turns that stream into
clean **noise events**: it detects the moment a session goes *silent → active*,
debounces so a continuous stream snitches once (not every tick), and records each
onset (`who · when · how loud`) into an in-memory ring buffer — also logged to
`%LOCALAPPDATA%\noise-snitch\noise-snitch.log` (visible via DebugView). Next up:
the tray **blotter UI** that renders those events (M4). See
[PLAN.md](./PLAN.md) for the roadmap and
[issues](https://github.com/rwrife/noise-snitch/issues) for milestones.

## Planned MVP (v0.1)

- Lives in the **system tray**, no main window.
- Detects when any app goes **silent → making sound**.
- Logs **timestamp · process name · peak level · session name**.
- A tray **flyout blotter** of recent noise events, newest first.
- Tray icon **flashes** when someone gets snitched on.

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
