using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoiseSnitch.Ui;

/// <summary>
/// Turns a raw process/executable name (e.g. <c>chrome</c>, <c>msedge.exe</c>,
/// <c>Discord</c>) into a human-friendly display name for the blotter (M5's
/// "friendly process names" item).
///
/// This is deliberately pure and WinForms-free so it's fully unit-testable and
/// can later back both the list rows and tooltips. Resolving actual app *icons*
/// needs the live process/exe on disk and is left to the WinForms layer; the
/// text mapping lives here.
///
/// Strategy:
/// <list type="number">
/// <item>Trim and drop a trailing <c>.exe</c> (case-insensitive).</item>
/// <item>Look the bare key up in a small curated table of the usual suspects.</item>
/// <item>Otherwise prettify: split on separators and Title-Case the words, so an
/// unknown <c>my_cool_app</c> reads as <c>My Cool App</c> rather than raw.</item>
/// </list>
/// </summary>
internal static class FriendlyName
{
    /// <summary>Shown for the special pid-0 "system sounds" session.</summary>
    public const string SystemSounds = "System sounds";

    /// <summary>Last-resort label when there's genuinely nothing to show.</summary>
    public const string Unknown = "Unknown app";

    // Curated map of common Windows audio offenders → friendly names. Keys are
    // lower-cased, .exe-stripped process names. Intentionally small; the
    // prettifier handles the long tail.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Firefox",
        ["brave"] = "Brave",
        ["opera"] = "Opera",
        ["iexplore"] = "Internet Explorer",
        ["discord"] = "Discord",
        ["slack"] = "Slack",
        ["teams"] = "Microsoft Teams",
        ["ms-teams"] = "Microsoft Teams",
        ["zoom"] = "Zoom",
        ["spotify"] = "Spotify",
        ["vlc"] = "VLC",
        ["wmplayer"] = "Windows Media Player",
        ["explorer"] = "Windows Explorer",
        ["outlook"] = "Outlook",
        ["steam"] = "Steam",
        ["skype"] = "Skype",
        ["whatsapp"] = "WhatsApp",
        ["telegram"] = "Telegram",
        ["code"] = "Visual Studio Code",
        ["devenv"] = "Visual Studio",
        ["obs64"] = "OBS Studio",
        ["obs"] = "OBS Studio",
    };

    /// <summary>
    /// Maps a raw process name (with or without <c>.exe</c>) to a friendly name.
    /// Falls back to a prettified version of the raw name, then to
    /// <see cref="Unknown"/> when the input is blank.
    /// </summary>
    public static string From(string? processName)
    {
        string bare = StripExe(processName?.Trim() ?? string.Empty);
        if (bare.Length == 0)
        {
            return Unknown;
        }

        return Known.TryGetValue(bare, out var friendly) ? friendly : Prettify(bare);
    }

    /// <summary>
    /// Resolves the display name for a <see cref="NoiseSnitch.Model.NoiseEvent"/>'s
    /// process, honouring the pid-0 system-sounds special case and falling back to
    /// <c>pid N</c> only when there is truly no name to show.
    /// </summary>
    public static string ForEvent(uint processId, string? processName)
    {
        if (processId == 0)
        {
            return SystemSounds;
        }

        string bare = StripExe(processName?.Trim() ?? string.Empty);
        return bare.Length == 0 ? $"pid {processId}" : From(bare);
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;

    private static string Prettify(string bare)
    {
        // Split on common word separators; collapse empties from runs of them.
        var parts = bare.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return bare;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = TitleWord(parts[i]);
        }

        return string.Join(' ', parts);
    }

    private static string TitleWord(string word)
    {
        // Leave already-mixed-case words (e.g. "iTunes", "OBS") alone; only
        // touch all-lower / all-upper single-shape words so we don't mangle
        // intentional casing.
        bool allLower = word.Equals(word.ToLowerInvariant(), StringComparison.Ordinal);
        if (!allLower)
        {
            return word;
        }

        return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..];
    }
}
