using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Config;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;
using NoiseSnitch.Persistence;
using NoiseSnitch.Personality;
using NoiseSnitch.Ui;

namespace NoiseSnitch.Tray;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/> and keeps the app alive with no
/// main window. As of M3 it starts the <see cref="SessionWatcher"/>, which detects
/// silent → active onsets and records <c>NoiseEvent</c>s into its
/// <see cref="SessionWatcher.Events"/> store (and the debug log). M4 renders those
/// events in a <see cref="BlotterForm"/> flyout opened from the icon. M5 flashes
/// the tray icon (via <see cref="FlashController"/>) on each new onset so a glance
/// at the tray shows something just made a sound.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string Tooltip = "noise-snitch is watching 👀";

    private readonly NotifyIcon _notifyIcon;
    private readonly SessionWatcher _watcher;
    private readonly BlotterForm _blotter;
    private readonly SettingsStore _settingsStore = new();
    private readonly NoiseLog? _log;

    // M5: tray icon flash on each new event. Two pre-rendered icons (calm +
    // lit) are swapped by a pure FlashController; a short WinForms timer restores
    // the calm icon once the flash window elapses. Both icons live for the app's
    // lifetime and are disposed on shutdown.
    private readonly Icon _restingIcon = TrayIcon.Create();
    private readonly Icon _flashIcon = TrayIcon.CreateFlash();
    private readonly FlashController _flash = new();
    private readonly System.Windows.Forms.Timer _flashTimer = new();
    private bool _showingFlash;

    // v0.2 "Mute-the-snitched" (issue #7): the tray owns the muter (which touches
    // WASAPI from this STA/UI thread) and remembers which pids we've muted so the
    // blotter can render them struck-through even across later events. The set is
    // best-effort UI state: a muted app that exits simply stops appearing.
    private readonly SessionMuter _muter = new();
    private readonly HashSet<uint> _mutedPids = new();

    // Issue #8 "Quiet-hours alerting": a pure schedule built from settings decides
    // whether an onset falls inside the user's focus window. When it does, we
    // escalate the onset with a loud tray balloon on top of the usual flash. Off
    // unless the user opted in and picked a window. The tray owns the toast; the
    // schedule owns the (unit-tested) "are we quiet right now?" decision.
    private readonly QuietHoursSchedule _quietHours;

    // Issue #24: the snitch's current "voice". Loaded from settings, resolvable
    // even from a corrupt/unknown key (falls back to the default pack), and
    // switchable at runtime from the tray's Personality submenu without a restart.
    private SnitchPersonality _personality;

    // Issue #28: system-wide hotkey to pop the blotter. A hidden native window
    // receives WM_HOTKEY and raises Pressed; we toggle the flyout near the cursor.
    // Null when the feature is disabled in settings or registration failed.
    private readonly HotkeyWindow? _hotkey;

    public TrayApplicationContext()
    {
        // M5: load persisted settings (poll interval, # events, thresholds).
        // Load never throws — a missing/corrupt file yields clamped defaults.
        Settings settings = _settingsStore.Load();
        // Write it back so the file exists on disk for users to discover & edit
        // (first run materializes a fully-populated settings.json).
        _settingsStore.Save(settings);
        DebugLog.Write(
            $"[settings] poll={settings.PollIntervalMs}ms keep={settings.EventsToKeep} " +
            $"peak={settings.PeakThreshold:0.000} release={settings.ReleaseMs}ms " +
            $"persist={settings.PersistLog} maxLog={settings.MaxLogBytes}B " +
            $"file={_settingsStore.FilePath ?? "<unavailable>"}");

        // M6: durable on-disk history is opt-in. Only build the log (and thus
        // only touch disk) when the user turned persistence on in settings.
        _log = settings.PersistLog
            ? new NoiseLog(maxBytes: settings.MaxLogBytes)
            : null;
        if (_log is not null)
        {
            DebugLog.Write($"[log] persistence ON → {_log.FilePath ?? "<unavailable>"} (cap {_log.MaxBytes}B)");
        }

        // Issue #8: build the quiet-hours schedule from settings. Escalation is a
        // no-op unless the user enabled it and configured a non-empty window.
        _quietHours = QuietHoursSchedule.FromSettings(settings);
        if (_quietHours.Enabled && !_quietHours.IsEmptyWindow)
        {
            DebugLog.Write(
                $"[quiet] quiet-hours ON {settings.QuietHoursStart}–{settings.QuietHoursEnd} " +
                "(local); onsets in-window escalate with a toast.");
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show blotter", null, OnShowBlotter);
        // Issue #24: resolve the persisted personality pack (fallback-safe).
        _personality = PersonalityCatalog.Resolve(settings.PersonalityPack);
        DebugLog.Write($"[personality] active pack={_personality.Key} ({_personality.DisplayName})");
        // Issue #22: aggregate "who keeps doing this?" view of today's noise.
        menu.Items.Add("Leaderboard", null, OnShowLeaderboard);
        // Issue #23: glanceable summary of today's noise activity.
        menu.Items.Add("Today's digest", null, OnShowDigest);
        // M6: copy the last hour of noise to the clipboard for easy reporting.
        // Shown only when persistence is on (there's nothing durable to export
        // otherwise).
        if (_log is not null)
        {
            menu.Items.Add("Copy last hour", null, OnCopyLastHour);
        }

        menu.Items.Add("About", null, OnAbout);
        menu.Items.Add(new ToolStripSeparator());
        // Issue #24: pick the snitch's voice; applies live (no restart).
        menu.Items.Add(BuildPersonalityMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, OnQuit);

        _notifyIcon = new NotifyIcon
        {
            // Tooltip text is capped at 63 chars by the Win32 shell; ours fits.
            Text = TrayTooltip(),
            Icon = _restingIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };

        // M5: the restore timer runs on this UI thread. It is armed only while a
        // flash is active; each tick re-asks the controller whether the window is
        // still open (a fresh onset may have extended it) and restores the calm
        // icon once it has elapsed.
        _flashTimer.Tick += OnFlashTimerTick;

        // M3: start watching. The watcher polls on a WinForms timer (ticks run on
        // this UI thread), detects silent → active onsets, and records them. M5:
        // the cadence, ring-buffer size, and detector tunables now come from the
        // persisted settings instead of hard-coded constants.
        _watcher = new SessionWatcher(
            interval: TimeSpan.FromMilliseconds(settings.PollIntervalMs),
            detectorOptions: new EdgeDetectorOptions(
                settings.PeakThreshold,
                TimeSpan.FromMilliseconds(settings.ReleaseMs)),
            events: new EventStore(settings.EventsToKeep),
            log: _log,
            // Issue #9: apps the user has silenced never reach the blotter or log.
            ignore: new IgnoreList(settings.IgnoredApps));

        // M4: the blotter flyout reads the watcher's event store. Created up front
        // (hidden) so opening it from the tray is instant and it can subscribe to
        // new onsets while visible. v0.2 (issue #7): it also gets callbacks to
        // query/toggle mute for a row's culprit, backed by the tray's muter.
        _blotter = new BlotterForm(
            _watcher.Events,
            isMuted: IsCulpritMuted,
            toggleMute: ToggleCulpritMute);
        // Issue #24: seed the blotter's empty-state from the active pack.
        _blotter.EmptyStateText = _personality.BlotterEmptyState;

        // M5: flash the tray icon whenever an onset is recorded. Subscribing to
        // the store (rather than the watcher) means every event that reaches the
        // blotter also flashes, in one place.
        _watcher.Events.Added += OnNoiseAdded;

        // Left-click or double-click the tray icon opens the blotter near the
        // cursor; right-click still shows the context menu. We use MouseClick (not
        // Click) so we get the screen position to anchor the flyout.
        _notifyIcon.MouseClick += OnIconMouseClick;
        _notifyIcon.DoubleClick += (_, _) => ShowBlotterAtCursor();

        _watcher.Start();

        // Issue #28: register the global hotkey (default Ctrl+Alt+N) to pop the
        // blotter from anywhere. Opt-out via settings; a clash is logged and the
        // shortcut is simply unavailable this session (no crash).
        if (settings.HotkeyEnabled)
        {
            _hotkey = new HotkeyWindow();
            _hotkey.Pressed += OnHotkeyPressed;
            if (_hotkey.TryRegister(settings.Hotkey))
            {
                DebugLog.Write($"[hotkey] blotter shortcut active: {settings.HotkeyCombo}");
            }
            else
            {
                // Keep the window around (harmless) but the shortcut is inert; the
                // failure is already logged in TryRegister.
            }
        }
    }

    /// <summary>
    /// Issue #28: the global hotkey fired — toggle the blotter near the cursor.
    /// Raised on the UI thread by the hidden hotkey window's message loop.
    /// </summary>
    private void OnHotkeyPressed(object? sender, EventArgs e) => _blotter.ToggleNear(Cursor.Position);

    private void OnIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowBlotterAtCursor();
        }
    }

    private void OnShowBlotter(object? sender, EventArgs e) => ShowBlotterAtCursor();

    /// <summary>
    /// M5 flash: an onset was recorded. Marshal onto the UI thread (the watcher
    /// polls on a WinForms timer that already runs here, but the store's event can
    /// in principle be raised from any thread that calls <c>Add</c>), light the
    /// tray icon, and (re)arm the restore timer for the flash window.
    /// </summary>
    private void OnNoiseAdded(object? sender, NoiseEvent e)
    {
        // The store's Added event is raised on whatever thread called Add — today
        // that's the watcher's WinForms timer (this UI thread), but we don't rely
        // on it. Marshal onto the UI thread via the always-alive blotter form
        // (NotifyIcon itself has no window handle to invoke against). If the
        // form's handle isn't up yet we must already be on the UI thread during
        // startup, so run inline.
        if (_blotter.IsHandleCreated && _blotter.InvokeRequired)
        {
            _blotter.BeginInvoke(new Action(() => OnNoiseAddedUi(e)));
        }
        else
        {
            OnNoiseAddedUi(e);
        }
    }

    /// <summary>
    /// UI-thread reaction to a new onset: flash the tray icon and, when the onset
    /// lands inside the configured quiet window (issue #8), escalate it with a
    /// loud balloon. Split from the marshalling in <see cref="OnNoiseAdded"/> so
    /// both effects share one hop onto the UI thread.
    /// </summary>
    private void OnNoiseAddedUi(NoiseEvent e)
    {
        FlashNow();
        MaybeAlertQuietHours(e);
    }

    /// <summary>
    /// Issue #8: if quiet hours are active <em>now</em> (local wall clock), raise
    /// a tray balloon naming the culprit so a sound during the user's focus/sleep
    /// window is impossible to miss. Uses <see cref="DateTime.Now"/> (local),
    /// because a quiet window is a human-schedule concept. A no-op when the
    /// feature is off or we're outside the window — the flash already happened.
    /// </summary>
    private void MaybeAlertQuietHours(NoiseEvent e)
    {
        if (!_quietHours.IsQuietAt(DateTime.Now))
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = QuietHoursAlertFormatter.AlertTitle;
        _notifyIcon.BalloonTipText = QuietHoursAlertFormatter.Body(e);
        _notifyIcon.ShowBalloonTip(3000);
        DebugLog.Write($"[quiet] escalated onset in-window: {e}");
    }

    /// <summary>Swaps to the lit icon and arms/extends the restore timer. UI thread only.</summary>
    private void FlashNow()
    {
        DateTime now = DateTime.UtcNow;
        _flash.Trigger(now);

        if (!_showingFlash)
        {
            _showingFlash = true;
            _notifyIcon.Icon = _flashIcon;
        }

        ArmFlashTimer(now);
    }

    /// <summary>
    /// Schedules the restore check for when the current flash window elapses. A
    /// WinForms timer needs a strictly-positive interval, so we floor it at 1ms.
    /// </summary>
    private void ArmFlashTimer(DateTime nowUtc)
    {
        int ms = (int)Math.Ceiling(_flash.RemainingUntil(nowUtc).TotalMilliseconds);
        _flashTimer.Stop();
        _flashTimer.Interval = Math.Max(1, ms);
        _flashTimer.Start();
    }

    /// <summary>
    /// Restore-timer tick: if a later onset pushed the flash window out, reschedule;
    /// otherwise the window has elapsed, so stop the timer and return to the calm icon.
    /// </summary>
    private void OnFlashTimerTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (_flash.IsFlashing(now))
        {
            ArmFlashTimer(now);
            return;
        }

        _flashTimer.Stop();
        RestoreRestingIcon();
    }

    /// <summary>Returns the tray to the calm resting icon. Idempotent. UI thread only.</summary>
    private void RestoreRestingIcon()
    {
        _flash.Reset();
        if (_showingFlash)
        {
            _showingFlash = false;
            _notifyIcon.Icon = _restingIcon;
        }
    }

    /// <summary>
    /// M6 "copy/export last hour": pull the last hour of events from the durable
    /// log, render the plain-text report, and drop it on the clipboard so the user
    /// can paste it into a bug report or message.
    /// </summary>
    private void OnCopyLastHour(object? sender, EventArgs e)
    {
        if (_log is null)
        {
            return;
        }

        var events = _log.ReadSince(TimeSpan.FromHours(1), DateTime.UtcNow);
        string report = NoiseExport.Report(events, NoiseExport.LastHourWindow, DateTime.UtcNow);

        _notifyIcon.BalloonTipTitle = "noise-snitch";
        try
        {
            // Clipboard can transiently fail if another app holds it open; treat
            // it as non-fatal and just tell the user.
            Clipboard.SetText(report);
            _notifyIcon.BalloonTipText = events.Count == 0
                ? "No noise in the last hour — nothing to copy."
                : $"Copied {events.Count} event(s) from the last hour to the clipboard.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException)
        {
            DebugLog.Write($"[log] clipboard copy failed: {ex.Message}");
            _notifyIcon.BalloonTipText = "Couldn't reach the clipboard — try again in a moment.";
        }

        _notifyIcon.ShowBalloonTip(3000);
    }

    private void ShowBlotterAtCursor() => _blotter.ShowNear(Cursor.Position);

    /// <summary>
    /// v0.2 (issue #7): does the blotter show this culprit as muted? Reflects our
    /// remembered set (what the user toggled in-app) rather than re-probing WASAPI
    /// on every repaint, which would be far too chatty for a paint path.
    /// </summary>
    private bool IsCulpritMuted(NoiseEvent e) => _mutedPids.Contains(e.ProcessId);

    /// <summary>
    /// v0.2 (issue #7): flip the mute state of the culprit behind a blotter row via
    /// the <see cref="SessionMuter"/>, update our remembered set to match the
    /// outcome, and show a short balloon so the user knows what happened. Runs on
    /// the UI thread (invoked from the blotter's menu click). Returns the outcome
    /// so the blotter can repaint immediately.
    /// </summary>
    private MuteOutcome ToggleCulpritMute(NoiseEvent e)
    {
        MuteOutcome outcome = _muter.Toggle(e.ProcessId);

        switch (outcome)
        {
            case MuteOutcome.Muted:
                _mutedPids.Add(e.ProcessId);
                break;
            case MuteOutcome.Unmuted:
            case MuteOutcome.NoSession:
                // No live session to mute (app went quiet/exited) or we just
                // unmuted: either way it's no longer silenced by us.
                _mutedPids.Remove(e.ProcessId);
                break;
            // SystemSoundsDeclined / Failed: leave remembered state untouched.
        }

        DebugLog.Write($"[mute] toggle pid {e.ProcessId} ({e.ProcessName}) -> {outcome}");

        _notifyIcon.BalloonTipTitle = "noise-snitch";
        _notifyIcon.BalloonTipText =
            MuteActionFormatter.Feedback(outcome, e.ProcessId, e.ProcessName);
        _notifyIcon.ShowBalloonTip(2000);

        return outcome;
    }

    // Issue #24: the tray tooltip comes from the active personality pack, but is
    // capped to the Win32 shell's 63-char NotifyIcon.Text limit so a long pack
    // string can't get truncated mid-emoji or rejected.
    private string TrayTooltip()
    {
        string t = _personality.TrayTooltip;
        return t.Length <= 63 ? t : t.Substring(0, 63);
    }

    // Issue #24: a checkable submenu listing every built-in pack. Selecting one
    // switches the snitch's voice live and persists the choice.
    private ToolStripMenuItem BuildPersonalityMenu()
    {
        var root = new ToolStripMenuItem("Personality");
        foreach (SnitchPersonality pack in PersonalityCatalog.All)
        {
            var item = new ToolStripMenuItem(pack.DisplayName)
            {
                Tag = pack.Key,
                Checked = pack.Key == _personality.Key,
                CheckOnClick = false,
            };
            item.Click += OnPersonalitySelected;
            root.DropDownItems.Add(item);
        }

        return root;
    }

    // Issue #24: apply a picked pack without a restart — retint the checkmarks,
    // refresh the tray tooltip and blotter empty-state, and persist the key.
    private void OnPersonalitySelected(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem clicked || clicked.Tag is not string key)
        {
            return;
        }

        _personality = PersonalityCatalog.Resolve(key);

        // Retick sibling items so exactly the active pack shows a check.
        if (clicked.OwnerItem is ToolStripMenuItem root)
        {
            foreach (ToolStripItem sibling in root.DropDownItems)
            {
                if (sibling is ToolStripMenuItem mi && mi.Tag is string k)
                {
                    mi.Checked = k == _personality.Key;
                }
            }
        }

        // Apply live.
        _notifyIcon.Text = TrayTooltip();
        _blotter.EmptyStateText = _personality.BlotterEmptyState;

        // Persist the choice (load-modify-save so we don't clobber other fields).
        Settings current = _settingsStore.Load();
        current.PersonalityPack = _personality.Key;
        _settingsStore.Save(current);
        DebugLog.Write($"[personality] switched to {_personality.Key} ({_personality.DisplayName})");
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        _notifyIcon.BalloonTipTitle = "noise-snitch";
        _notifyIcon.BalloonTipText = DebugLog.FilePath is { } path
            ? $"Watching audio sessions. Log: {path}"
            : "Watching for which app just made that sound.";
        _notifyIcon.ShowBalloonTip(3000);
    }

    /// <summary>
    /// Issue #22: rank today's apps by number of noise events and show them in a
    /// simple dialog. Ranking/formatting are pure (see <see cref="Leaderboard"/>
    /// and <see cref="LeaderboardFormatter"/>); this only sources the events and
    /// puts the rendered text on screen.
    /// </summary>
    private void OnShowLeaderboard(object? sender, EventArgs e)
    {
        var rows = Leaderboard.ForDay(_watcher.Events.Recent(), DateTime.UtcNow);
        MessageBox.Show(
            LeaderboardFormatter.Render(rows),
            "Noise leaderboard — today",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Issue #23: summarize today's noise (total, top offenders, percentage
    /// shares) and show it in a simple dialog. Aggregation/formatting are pure
    /// (see <see cref="DigestBuilder"/> and <see cref="DigestFormatter"/>); this
    /// only sources the events and puts the rendered text on screen.
    /// </summary>
    private void OnShowDigest(object? sender, EventArgs e)
    {
        var digest = DigestBuilder.ForDay(_watcher.Events.Recent(), DateTime.UtcNow);
        MessageBox.Show(
            DigestFormatter.Render(digest),
            "Today's digest",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnQuit(object? sender, EventArgs e) => ExitThread();

    protected override void ExitThreadCore()
    {
        // Stop polling and hide before disposing so the icon doesn't linger in
        // the tray as a ghost until the user hovers over it.
        _watcher.Events.Added -= OnNoiseAdded;
        _flashTimer.Stop();
        if (_hotkey is not null)
        {
            _hotkey.Pressed -= OnHotkeyPressed;
            _hotkey.Dispose();
        }
        _watcher.Dispose();
        _muter.Dispose();
        _blotter.HideFlyout();
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Events.Added -= OnNoiseAdded;
            _flashTimer.Dispose();
            if (_hotkey is not null)
            {
                _hotkey.Pressed -= OnHotkeyPressed;
                _hotkey.Dispose();
            }
            _watcher.Dispose();
            _muter.Dispose();
            _blotter.Dispose();
            _notifyIcon.Dispose();
            _restingIcon.Dispose();
            _flashIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
