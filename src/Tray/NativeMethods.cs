using System;
using System.Runtime.InteropServices;

namespace NoiseSnitch.Tray;

/// <summary>
/// Minimal Win32 interop. <see cref="DestroyIcon"/> releases the unmanaged icon
/// handle produced by <c>Bitmap.GetHicon()</c> so we don't leak GDI handles.
/// </summary>
internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);

    // Issue #28: global-hotkey registration. RegisterHotKey binds a modifier+key
    // combo to a window so Windows posts WM_HOTKEY when it's pressed anywhere;
    // UnregisterHotKey releases it. Both report success via the return value.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
