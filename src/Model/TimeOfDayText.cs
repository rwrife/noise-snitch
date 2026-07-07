using System;
using System.Globalization;

namespace NoiseSnitch.Model;

/// <summary>
/// Pure parsing/formatting of a wall-clock time-of-day written as <c>"HH:mm"</c>
/// (24-hour, zero-padded, e.g. <c>"22:00"</c>, <c>"07:30"</c>) — the human-editable
/// form the quiet-hours window (issue #8) is stored in inside
/// <see cref="Settings"/>.
///
/// Kept free of any UI/runtime dependency (like <see cref="NoiseSnitch.Ui.RelativeTime"/>)
/// so the "what does this string mean, in minutes past midnight" rule is one pure,
/// unit-tested place that <see cref="Settings"/> normalization and any future UI
/// both share. Being forgiving matters here: this string is meant to be typed by
/// hand into <c>settings.json</c>, so a stray space or a single-digit hour should
/// still parse rather than silently disable the feature.
/// </summary>
internal static class TimeOfDayText
{
    /// <summary>Minutes in a day; a valid minute-of-day is in <c>[0, 1440)</c>.</summary>
    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Parses <paramref name="text"/> as a 24-hour <c>H:mm</c>/<c>HH:mm</c> time
    /// into minutes past midnight (<c>[0, 1440)</c>), or returns
    /// <paramref name="fallbackMinuteOfDay"/> when it can't. Accepts surrounding
    /// whitespace and a single- or double-digit hour; rejects out-of-range hours
    /// (&gt;23) or minutes (&gt;59) rather than wrapping them, since an out-of-range
    /// value is a typo, not an intent.
    /// </summary>
    public static int ParseToMinuteOfDay(string? text, int fallbackMinuteOfDay)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallbackMinuteOfDay;
        }

        string trimmed = text.Trim();
        int colon = trimmed.IndexOf(':');
        if (colon <= 0 || colon == trimmed.Length - 1)
        {
            return fallbackMinuteOfDay;
        }

        string hourPart = trimmed[..colon];
        string minutePart = trimmed[(colon + 1)..];

        if (!int.TryParse(hourPart, NumberStyles.None, CultureInfo.InvariantCulture, out int hour) ||
            !int.TryParse(minutePart, NumberStyles.None, CultureInfo.InvariantCulture, out int minute))
        {
            return fallbackMinuteOfDay;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return fallbackMinuteOfDay;
        }

        return (hour * 60) + minute;
    }

    /// <summary>
    /// Renders a minute-of-day back to canonical zero-padded <c>"HH:mm"</c>. Any
    /// out-of-range input is wrapped into a valid day first so this never throws.
    /// </summary>
    public static string FromMinuteOfDay(int minuteOfDay)
    {
        int m = minuteOfDay % MinutesPerDay;
        if (m < 0)
        {
            m += MinutesPerDay;
        }

        int hour = m / 60;
        int minute = m % 60;
        return $"{hour:00}:{minute:00}";
    }
}
