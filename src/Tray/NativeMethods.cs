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
}
