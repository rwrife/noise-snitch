using System;
using System.Drawing;
using System.Windows.Forms;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Diagnostics;
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

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show blotter", null, OnShowBlotter);
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
        // this UI thread), detects silent → active onsets, and records them.
        _watcher = new SessionWatcher();

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
