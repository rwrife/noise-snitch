using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NoiseSnitch.Tray;

/// <summary>
/// Produces the tray icon at runtime so M1 ships without a binary .ico asset.
/// It draws a small "ear" glyph (the snitch is listening). Later milestones can
/// swap in a designed multi-resolution icon and add a flash/badge state.
/// </summary>
internal static class TrayIcon
{
    /// <summary>
    /// Renders a 32x32 icon. The handle returned by <see cref="Icon.FromHandle"/>
    /// is owned by the bitmap, which we keep alive for the icon's lifetime by
    /// cloning into a standalone <see cref="Icon"/>.
    /// </summary>
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Dark rounded background so the glyph reads on light and dark taskbars.
            using (var bg = new SolidBrush(Color.FromArgb(28, 30, 34)))
            {
                g.FillEllipse(bg, 1, 1, 30, 30);
            }

            // A simple "C"-shaped ear in a warm accent colour.
            using var pen = new Pen(Color.FromArgb(255, 196, 0), 4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawArc(pen, 9, 7, 16, 18, -70, 250);
            using (var dot = new SolidBrush(Color.FromArgb(255, 196, 0)))
            {
                g.FillEllipse(dot, 14, 17, 5, 5);
            }
        }

        // FromHandle's icon shares the GDI handle with the bitmap; clone so the
        // returned Icon stays valid after the bitmap is disposed.
        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
