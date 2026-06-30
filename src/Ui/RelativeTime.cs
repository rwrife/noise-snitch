using System;

namespace NoiseSnitch.Ui;

/// <summary>
/// Formats a past <see cref="DateTime"/> as a short, human "… ago" string for
/// the blotter (M4) — e.g. <c>now</c>, <c>3s ago</c>, <c>4m ago</c>, <c>2h ago</c>,
/// <c>5d ago</c>.
///
/// Kept deliberately free of any WinForms / UI dependency so the rounding and
/// pluralization rules are pure and unit-testable. The form passes
/// <see cref="DateTime.UtcNow"/> as <c>now</c> at render time; tests pass a fixed
/// clock.
/// </summary>
internal static class RelativeTime
{
    /// <summary>
    /// Renders <paramref name="timestampUtc"/> relative to <paramref name="nowUtc"/>.
    /// Both are treated as UTC instants. A future or equal timestamp (clock skew,
    /// or an event from "this" tick) renders as <c>now</c> rather than a negative
    /// value.
    /// </summary>
    public static string Format(DateTime timestampUtc, DateTime nowUtc)
    {
        TimeSpan delta = nowUtc - timestampUtc;

        // Future / just-now: never show negatives. Anything under a second reads
        // as "now" — the blotter doesn't need sub-second precision.
        if (delta < TimeSpan.FromSeconds(1))
        {
            return "now";
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return $"{(int)delta.TotalSeconds}s ago";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return $"{(int)delta.TotalDays}d ago";
    }
}
