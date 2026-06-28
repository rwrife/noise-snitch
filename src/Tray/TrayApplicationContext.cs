using System;
using System.Drawing;
using System.Windows.Forms;

namespace NoiseSnitch.Tray;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/> and keeps the app alive with no
/// main window. Later milestones will subscribe this to audio events, flash the
/// icon, and open the blotter flyout; for M1 it just runs and offers Quit.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string Tooltip = "noise-snitch is watching 👀";

    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("noise-snitch", null, OnAbout);
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

        // Double-clicking the tray icon will later open the blotter; for now it
        // just surfaces the same "about" balloon so the icon feels responsive.
        _notifyIcon.DoubleClick += OnAbout;
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        _notifyIcon.BalloonTipTitle = "noise-snitch";
        _notifyIcon.BalloonTipText = "Watching for which app just made that sound.";
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void OnQuit(object? sender, EventArgs e) => ExitThread();

    protected override void ExitThreadCore()
    {
        // Hide before disposing so the icon doesn't linger in the tray as a
        // ghost until the user hovers over it.
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
