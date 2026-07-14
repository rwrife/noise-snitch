using System;
using System.Collections.Generic;
using System.Linq;
using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// Builds an end-of-day (or on-demand) digest of noise activity: total event
/// count plus a per-app breakdown with counts and percentage shares, sorted by
/// count descending (issue #23).
///
/// A cheap read layer on top of the same <see cref="NoiseEvent"/> stream the
/// blotter (#4) and leaderboard (#22) consume. Aggregation reuses
/// <see cref="Leaderboard"/> so app-identity bucketing (pid-0 system session,
/// <c>.exe</c>-stripped case-insensitive names) and deterministic tie-breaking
/// stay identical across the two views.
///
/// Pure and WinForms-free so it is fully unit-testable without any live audio or
/// UI. Text rendering lives in <see cref="DigestFormatter"/>.
/// </summary>
internal static class DigestBuilder
{
    /// <summary>
    /// Aggregates all supplied events (no window filtering) into a digest.
    /// Prefer <see cref="ForDay"/> for the default "today since local midnight"
    /// view.
    /// </summary>
    public static NoiseDigest Build(IEnumerable<NoiseEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return FromRows(Leaderboard.Rank(events));
    }

    /// <summary>
    /// Aggregates the subset of <paramref name="events"/> whose local calendar
    /// date matches that of <paramref name="nowUtc"/> — the default "today"
    /// window. Timestamps are compared in local time so the digest lines up with
    /// the clock on the user's taskbar.
    /// </summary>
    public static NoiseDigest ForDay(IEnumerable<NoiseEvent> events, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(events);
        return FromRows(Leaderboard.ForDay(events, nowUtc));
    }

    /// <summary>
    /// Turns ranked <see cref="LeaderboardRow"/>s (already count-descending,
    /// tie-broken by display name) into a digest by summing the total and
    /// attaching each app's rounded percentage share. Percentages use the
    /// standard midpoint-rounds-to-even <see cref="Math.Round(double)"/> and so
    /// need not sum to exactly 100.
    /// </summary>
    private static NoiseDigest FromRows(IReadOnlyList<LeaderboardRow> rows)
    {
        int total = rows.Sum(r => r.Count);
        if (total == 0)
        {
            return new NoiseDigest(0, Array.Empty<DigestRow>());
        }

        var breakdown = rows
            .Select(r => new DigestRow(
                r.ProcessId,
                r.ProcessName,
                r.Count,
                (int)Math.Round(r.Count * 100.0 / total, MidpointRounding.AwayFromZero)))
            .ToList();

        return new NoiseDigest(total, breakdown);
    }
}

/// <summary>
/// The aggregated result of a <see cref="DigestBuilder"/> run: the total number
/// of noise events in the window plus the per-app <see cref="DigestRow"/>
/// breakdown, sorted by count descending. Pure data for
/// <see cref="DigestFormatter"/> to render.
/// </summary>
/// <param name="Total">Total noise events counted in the window.</param>
/// <param name="Breakdown">Per-app slices, count descending. Empty when <see cref="Total"/> is 0.</param>
internal readonly record struct NoiseDigest(
    int Total,
    IReadOnlyList<DigestRow> Breakdown);
