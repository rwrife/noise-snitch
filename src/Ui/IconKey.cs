using System;

namespace NoiseSnitch.Ui;

/// <summary>
/// Pure, WinForms-free helpers for the M5 blotter icon cache: they decide
/// whether a captured executable path is worth trying to extract an icon from,
/// and normalize it into a stable cache key so two spellings of the same path
/// (case / trailing separators) share one cached bitmap.
///
/// Kept deliberately free of <c>System.Drawing</c> so the decision logic is
/// unit-testable without the Windows desktop runtime. The actual icon
/// extraction lives in <see cref="AppIconProvider"/>.
/// </summary>
internal static class IconKey
{
    /// <summary>
    /// True when <paramref name="executablePath"/> looks like a concrete file
    /// path we could plausibly pull an icon from. Rejects blanks and the
    /// best-effort placeholder strings the enumerator emits when a process has
    /// exited or its module path was unreadable (e.g. <c>pid:1234</c>,
    /// <c>System Sounds</c>) — those are process *names*, not paths, and must not
    /// be handed to the shell icon APIs.
    /// </summary>
    public static bool IsResolvable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        string p = executablePath.Trim();

        // Placeholder / name-only fallbacks are never real paths.
        if (p.StartsWith("pid:", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith("(exited)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A real captured module path is rooted (absolute). Bare names like
        // "chrome" or "chrome.exe" are not something we can locate on disk here.
        if (!LooksRooted(p))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes a path into a case-insensitive cache key: trims surrounding
    /// whitespace, unifies separators to <c>\</c> (Windows), and strips a
    /// trailing separator. Returns an empty string for inputs that are not
    /// <see cref="IsResolvable(string?)"/>, so callers can use empty to mean
    /// "don't bother / use the fallback glyph".
    /// </summary>
    public static string Normalize(string? executablePath)
    {
        if (!IsResolvable(executablePath))
        {
            return string.Empty;
        }

        string p = executablePath!.Trim().Replace('/', '\\');

        if (p.Length > 3 && p.EndsWith('\\'))
        {
            p = p.TrimEnd('\\');
        }

        return p.ToLowerInvariant();
    }

    // A rooted path is either a drive spec ("C:\...", "C:/...") or a UNC share
    // ("\\server\share"). We keep this string-based (not Path.IsPathRooted) so it
    // behaves identically regardless of the host OS running the tests.
    private static bool LooksRooted(string p)
    {
        if (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' &&
            (p[2] == '\\' || p[2] == '/'))
        {
            return true;
        }

        if (p.StartsWith("\\\\", StringComparison.Ordinal) ||
            p.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
