using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace NoiseSnitch.Ui;

/// <summary>
/// Resolves and caches per-application icons for the M5 blotter, closing out the
/// last "friendly process names + app icons" item on that milestone.
///
/// Given the best-effort executable path captured on a
/// <see cref="NoiseSnitch.Model.NoiseEvent"/>, it pulls the associated shell icon
/// (<see cref="Icon.ExtractAssociatedIcon(string)"/>), renders it once into a
/// small square bitmap at the requested size, and caches it keyed by the
/// normalized path (see <see cref="IconKey"/>) so a chatty app is only extracted
/// once. Everything is best-effort: any failure (missing file, access denied,
/// blank/placeholder path) yields <c>null</c> and the blotter falls back to a
/// generic glyph — an icon is a nicety, never load-bearing.
///
/// Not thread-safe; intended to be used from the UI thread that owns the blotter.
/// </summary>
internal sealed class AppIconProvider : IDisposable
{
    private readonly int _size;

    // Normalized path -> rendered icon (or null when extraction failed). We cache
    // the *negative* result too, so a path that can't yield an icon isn't retried
    // on every repaint.
    private readonly Dictionary<string, Image?> _cache = new(StringComparer.Ordinal);

    private bool _disposed;

    /// <param name="size">
    /// Square edge length, in pixels, to render each icon at (blotter row art).
    /// </param>
    public AppIconProvider(int size = 24)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Icon size must be positive.");
        }

        _size = size;
    }

    /// <summary>
    /// Returns a cached, <see cref="_size"/>-square icon for the given executable
    /// path, extracting it on first request. Returns <c>null</c> when the path is
    /// blank/placeholder, the file is gone, or extraction fails for any reason —
    /// callers should draw their fallback glyph in that case. The returned image
    /// is owned by this provider; do not dispose it.
    /// </summary>
    public Image? Get(string? executablePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string key = IconKey.Normalize(executablePath);
        if (key.Length == 0)
        {
            return null;
        }

        if (_cache.TryGetValue(key, out Image? cached))
        {
            return cached;
        }

        Image? rendered = TryExtract(executablePath!);
        _cache[key] = rendered; // may be null: remembered so we don't retry.
        return rendered;
    }

    private Image? TryExtract(string path)
    {
        try
        {
            // ExtractAssociatedIcon needs the file to exist; guard first so the
            // common "process already exited" case is a cheap miss, not an
            // exception per repaint.
            if (!File.Exists(path))
            {
                return null;
            }

            using Icon? icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            // Render into a fixed-size 32bpp bitmap so rows stay aligned and we
            // don't hold a big/variable native icon handle for the app's lifetime.
            var bmp = new Bitmap(_size, _size);
            try
            {
                using var g = Graphics.FromImage(bmp);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawIcon(icon, new Rectangle(0, 0, _size, _size));
            }
            catch
            {
                bmp.Dispose();
                throw;
            }

            return bmp;
        }
        catch
        {
            // Access denied, malformed icon, path too long, transient IO — all
            // non-fatal. The blotter simply shows its generic glyph.
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (Image? img in _cache.Values)
        {
            img?.Dispose();
        }

        _cache.Clear();
    }
}
