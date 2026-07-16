using System;
using System.Windows.Forms;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;

namespace NoiseSnitch.Tray;

/// <summary>
/// Issue #28: owns the system-wide hotkey that pops the blotter. A hidden
/// <see cref="NativeWindow"/> gives us a message-only-style handle to hand to the
/// Win32 <c>RegisterHotKey</c> API; when the user presses the combo, Windows posts
/// <c>WM_HOTKEY</c> to that handle and we raise <see cref="Pressed"/> so the tray
/// can toggle the flyout.
///
/// Robustness: <see cref="TryRegister"/> never throws. If the combo is already
/// owned by another app (or registration otherwise fails), it logs and returns
/// <c>false</c> — the app keeps running, just without the shortcut. Registration
/// is cleanly undone on <see cref="Dispose"/> (and before any re-register), so we
/// never leak a global hotkey.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ModNoRepeat = 0x4000; // MOD_NOREPEAT: don't auto-repeat on hold.

    // A fixed per-process id for our single hotkey. RegisterHotKey ids only need to
    // be unique within the window, and we register exactly one.
    private const int HotkeyId = 0xB10D; // "BLOD" ~ blotter; arbitrary but stable.

    private bool _registered;
    private bool _disposed;

    /// <summary>Raised (on the UI thread) each time the registered combo is pressed.</summary>
    public event EventHandler? Pressed;

    public HotkeyWindow()
    {
        // Create a real (hidden) window handle to receive WM_HOTKEY. A parameterless
        // CreateHandle with a default CreateParams yields a message-capable window;
        // it's never shown.
        CreateHandle(new CreateParams());
    }

    /// <summary>
    /// Registers <paramref name="hotkey"/> as the process-wide shortcut, replacing
    /// any previously-registered combo. Returns <c>true</c> on success; a failure
    /// (combo taken, no handle) is logged and swallowed. Never throws.
    /// </summary>
    public bool TryRegister(Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (_disposed)
        {
            return false;
        }

        Unregister();

        if (Handle == IntPtr.Zero)
        {
            DebugLog.Write("[hotkey] no window handle; global hotkey unavailable.");
            return false;
        }

        // MOD_NOREPEAT keeps a held combo from spamming WM_HOTKEY.
        bool ok = NativeMethods.RegisterHotKey(
            Handle, HotkeyId, hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey);

        if (ok)
        {
            _registered = true;
            DebugLog.Write($"[hotkey] registered {hotkey} (mods=0x{hotkey.Modifiers:X} vk=0x{hotkey.VirtualKey:X2}).");
        }
        else
        {
            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            DebugLog.Write(
                $"[hotkey] could not register {hotkey} (Win32 error {err}; combo likely already in use). " +
                "Shortcut disabled this session.");
        }

        return ok;
    }

    /// <summary>Releases the current registration if any. Idempotent; never throws.</summary>
    public void Unregister()
    {
        if (!_registered || Handle == IntPtr.Zero)
        {
            _registered = false;
            return;
        }

        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
