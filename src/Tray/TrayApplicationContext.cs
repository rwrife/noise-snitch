using System;
using System.Drawing;
using System.Windows.Forms;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Config;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;
using NoiseSnitch.Persistence;
using NoiseSnitch.Ui;

namespace NoiseSnitch.Tray;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/> and keeps the app alive with no
/// main window. As of M3 it starts the <see cref="SessionWatcher"/>, which detects
/// silent → active onsets and records <c>NoiseEvent</c>s into its
/// <see cref="SessionWatcher.Events"/> store (and the debug log). M4 renders those
/// events in a <see cref="BlotterForm"/> flyout opened from the icon.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string Tooltip = "noise-snitch is watching 👀";

    private readonly NotifyIcon _notifyIcon;
    private readonly SessionWatcher _watcher;
    private readonly BlotterForm _blotter;
    private readonly SettingsStore _settingsStore = new();
    private readonly NoiseLog? _log;

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

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show blotter", null, OnShowBlotter);
        // M6: copy the last hour of noise to the clipboard for easy reporting.
        // Shown only when persistence is on (there's nothing durable to export
        // otherwise).
        if (_log is not null)
        {
            menu.Items.Add("Copy last hour", null, OnCopyLastHour);
        }

        menu.Items.Add("About", null, OnAbout);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, OnQuit);

        _notifyIcon = new NotifyIcon
        {
            // Tooltip text is capped at 63 chars by the Win32 shell; ours fits.
            Text = Tooltip,
            Icon = TrayIcon.Create(),
            ContextMenuStrip = menu,
            Visible = true,
        };

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
            log: _log);

        // M4: the blotter flyout reads the watcher's event store. Created up front
        // (hidden) so opening it from the tray is instant and it can subscribe to
        // new onsets while visible.
        _blotter = new BlotterForm(_watcher.Events);

        // Left-click or double-click the tray icon opens the blotter near the
        // cursor; right-click still shows the context menu. We use MouseClick (not
        // Click) so we get the screen position to anchor the flyout.
        _notifyIcon.MouseClick += OnIconMouseClick;
        _notifyIcon.DoubleClick += (_, _) => ShowBlotterAtCursor();

        _watcher.Start();
    }

    private void OnIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowBlotterAtCursor();
        }
    }

    private void OnShowBlotter(object? sender, EventArgs e) => ShowBlotterAtCursor();

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

    private void OnAbout(object? sender, EventArgs e)
    {
        _notifyIcon.BalloonTipTitle = "noise-snitch";
        _notifyIcon.BalloonTipText = DebugLog.FilePath is { } path
            ? $"Watching audio sessions. Log: {path}"
            : "Watching for which app just made that sound.";
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void OnQuit(object? sender, EventArgs e) => ExitThread();

    protected override void ExitThreadCore()
    {
        // Stop polling and hide before disposing so the icon doesn't linger in
        // the tray as a ghost until the user hovers over it.
        _watcher.Dispose();
        _blotter.HideFlyout();
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _blotter.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
