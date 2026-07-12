using System;
using System.Collections.Generic;
using System.Linq;
using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// Aggregates <see cref="NoiseEvent"/>s into a ranked "who keeps doing this?"
/// leaderboard: apps ordered by how many noise events they produced within a
/// window (default: today). Complements the per-event blotter with an aggregate
/// view (issue #22).
///
/// Pure and WinForms-free so it is fully unit-testable without any live audio or
/// UI. Text rendering lives in <see cref="LeaderboardFormatter"/>.
///
/// Grouping: events are keyed by app identity, not by raw <c>ProcessId</c>
/// (pids are recycled). The pid-0 system-sounds session is its own bucket; every
/// other event is bucketed by its case-insensitive, <c>.exe</c>-stripped process
/// name so <c>chrome</c> and <c>chrome.exe</c> collapse into one row. Ordering is
/// deterministic: count descending, then friendly display name ascending
/// (culture-invariant, case-insensitive) for ties.
/// </summary>
internal static class Leaderboard
{
    /// <summary>
    /// Ranks all supplied events (no window filtering). Prefer
    /// <see cref="ForDay"/> for the default "today" view.
    /// </summary>
    public static IReadOnlyList<LeaderboardRow> Rank(IEnumerable<NoiseEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        // Bucket by app identity. Key is (pid==0) ? "\0system" : lower bare name.
        // We keep a representative pid/name per bucket for the row + display.
        var buckets = new Dictionary<string, (uint Pid, string Name, int Count)>(StringComparer.Ordinal);

        foreach (var e in events)
        {
            string key = KeyFor(e.ProcessId, e.ProcessName);
            if (buckets.TryGetValue(key, out var b))
            {
                buckets[key] = (b.Pid, b.Name, b.Count + 1);
            }
            else
            {
                buckets[key] = (e.ProcessId, e.ProcessName ?? string.Empty, 1);
            }
        }

        return buckets.Values
            .Select(b => (b.Pid, b.Name, b.Count, Display: FriendlyName.ForEvent(b.Pid, b.Name)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Display, StringComparer.InvariantCultureIgnoreCase)
            .Select((x, i) => new LeaderboardRow(i + 1, x.Pid, x.Name, x.Count))
            .ToList();
    }

    /// <summary>
    /// Ranks the subset of <paramref name="events"/> whose local calendar date
    /// matches that of <paramref name="nowUtc"/> — the default "today" window.
    /// Timestamps are compared in local time so a user's "today" lines up with
    /// the clock on their taskbar.
    /// </summary>
    public static IReadOnlyList<LeaderboardRow> ForDay(IEnumerable<NoiseEvent> events, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(events);
        DateTime today = nowUtc.ToLocalTime().Date;
        return Rank(events.Where(e => e.TimestampUtc.ToLocalTime().Date == today));
    }

    private static string KeyFor(uint pid, string? processName)
    {
        if (pid == 0)
        {
            return "\0system";
        }

        string bare = (processName ?? string.Empty).Trim();
        if (bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            bare = bare[..^4];
        }

        // Empty-name events (name couldn't be resolved) fall back to their pid so
        // two distinct nameless apps don't merge into one bogus bucket.
        return bare.Length == 0
            ? $"\0pid:{pid}"
            : bare.ToLowerInvariant();
    }
}
