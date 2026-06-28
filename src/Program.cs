using System;
using System.Windows.Forms;
using NoiseSnitch.Tray;

namespace NoiseSnitch;

/// <summary>
/// noise-snitch entry point.
///
/// M1 scope: boot a tray-only WinForms app that shows a <see cref="NotifyIcon"/>
/// with a "noise-snitch is watching 👀" tooltip and a Quit menu item. There is
/// deliberately no main window — the <see cref="ApplicationContext"/> keeps the
/// message loop alive while only the tray icon is visible.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
