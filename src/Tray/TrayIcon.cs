using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NoiseSnitch.Tray;

/// <summary>
/// Produces the tray icons at runtime so the app ships without binary <c>.ico</c>
/// assets. It draws a small "ear" glyph (the snitch is listening) in two states:
/// a calm <see cref="Create">resting</see> icon and a brighter, ringed
/// <see cref="CreateFlash">flash</see> icon shown briefly on each new noise event
/// (M5). Later milestones can swap in designed multi-resolution icons; the two
/// entry points and their meaning stay the same.
/// </summary>
internal static class TrayIcon
{
    // Shared geometry so the resting and flash icons are pixel-aligned and only
    // differ in colour/emphasis (nothing "jumps" when we swap them).
    private const int Size = 32;

    /// <summary>
    /// Renders the calm 32x32 resting icon — a warm ear glyph on a dark disc.
    /// This is what sits in the tray when nothing has made noise recently.
    /// </summary>
    public static Icon Create() => Render(flash: false);

    /// <summary>
    /// Renders the 32x32 <em>flash</em> icon: the same glyph, but lit up (bright
    /// accent fill + a glowing ring) so a quick glance at the tray registers
    /// "something just made a sound". Swapped in for
    /// <see cref="FlashController.FlashDuration"/> after each onset, then replaced
    /// by <see cref="Create"/>.
    /// </summary>
    public static Icon CreateFlash() => Render(flash: true);

    private static Icon Render(bool flash)
    {
        // Warm accent; the flash state pushes it toward white-hot and adds a ring.
        Color accent = flash
            ? Color.FromArgb(255, 224, 92)   // brighter, "lit" amber
            : Color.FromArgb(255, 196, 0);   // calm amber
        Color background = flash
            ? Color.FromArgb(60, 48, 8)      // subtly warmed backdrop when lit
            : Color.FromArgb(28, 30, 34);    // neutral dark disc at rest

        using var bmp = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Dark rounded background so the glyph reads on light and dark taskbars.
            using (var bg = new SolidBrush(background))
            {
                g.FillEllipse(bg, 1, 1, Size - 2, Size - 2);
            }

            // A glowing ring only in the flash state, to make the change obvious
            // even at tiny tray sizes where subtle colour shifts get lost.
            if (flash)
            {
                using var ring = new Pen(Color.FromArgb(180, accent), 2f);
                g.DrawEllipse(ring, 2, 2, Size - 5, Size - 5);
            }

            // A simple "C"-shaped ear in the accent colour.
            using var pen = new Pen(accent, 4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawArc(pen, 9, 7, 16, 18, -70, 250);
            using (var dot = new SolidBrush(accent))
            {
                g.FillEllipse(dot, 14, 17, 5, 5);
            }
        }

        // FromHandle's icon shares the GDI handle with the bitmap; clone so the
        // returned Icon stays valid after the bitmap is disposed, and destroy the
        // interim handle so we don't leak a GDI object.
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
