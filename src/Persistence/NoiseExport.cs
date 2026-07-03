using System;
using System.Collections.Generic;
using System.Text;
using NoiseSnitch.Model;
using NoiseSnitch.Ui;

namespace NoiseSnitch.Persistence;

/// <summary>
/// Pure text rendering for the M6 "copy/export the last hour" action: turns a set
/// of <see cref="NoiseEvent"/>s into a tidy, clipboard-ready plain-text report a
/// user can paste into a bug report or message ("here's every sound my PC made in
/// the last hour").
///
/// <para>Kept WinForms-free (the tray just copies the returned string to the
/// clipboard) so the wording, ordering, and the per-app tally are fully
/// unit-testable.</para>
/// </summary>
internal static class NoiseExport
{
    /// <summary>Header/window label used when the export covers "the last hour."</summary>
    public const string LastHourWindow = "last hour";

    /// <summary>
    /// Builds the report for <paramref name="events"/> (expected newest-first, as
    /// returned by <see cref="NoiseLog.ReadSince"/>), labelled with
    /// <paramref name="windowLabel"/> and timestamped at <paramref name="nowUtc"/>.
    ///
    /// Layout:
    /// <code>
    /// noise-snitch — 3 events (last hour) as of 14:05:12
    ///
    ///   14:05:07  Google Chrome        peak 0.42
    ///   14:03:55  Slack                peak 0.31
    ///   13:58:20  System sounds        peak 0.88
    ///
    /// Top offenders: Google Chrome ×1, Slack ×1, System sounds ×1
    /// </code>
    /// An empty set yields a friendly one-liner instead of an empty block.
    /// </summary>
    public static string Report(
        IReadOnlyList<NoiseEvent> events,
        string windowLabel,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(events);

        string stamp = nowUtc.ToLocalTime().ToString("HH:mm:ss");
        if (events.Count == 0)
        {
            return $"noise-snitch — no events in the {windowLabel} as of {stamp} 🤫";
        }

        var sb = new StringBuilder();
        sb.Append("noise-snitch — ")
          .Append(events.Count)
          .Append(events.Count == 1 ? " event (" : " events (")
          .Append(windowLabel)
          .Append(") as of ")
          .Append(stamp)
          .Append('\n')
          .Append('\n');

        // Column-align the friendly names so peaks line up; cap the pad so one
        // pathologically long name doesn't blow the layout out.
        var names = new string[events.Count];
        int width = 0;
        for (int i = 0; i < events.Count; i++)
        {
            names[i] = FriendlyName.ForEvent(events[i].ProcessId, events[i].ProcessName);
            width = Math.Max(width, names[i].Length);
        }

        width = Math.Min(width, 28);

        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();

        for (int i = 0; i < events.Count; i++)
        {
            NoiseEvent e = events[i];
            string when = e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
            sb.Append("  ")
              .Append(when)
              .Append("  ")
              .Append(names[i].PadRight(width))
              .Append("  peak ")
              .Append(e.Peak.ToString("0.00"))
              .Append('\n');

            if (!tally.TryGetValue(names[i], out int n))
            {
                order.Add(names[i]);
            }

            tally[names[i]] = n + 1;
        }

        sb.Append('\n').Append("Top offenders: ");
        // Most frequent first; ties keep first-seen (newest-first) order.
        order.Sort((a, b) => tally[b].CompareTo(tally[a]));
        for (int i = 0; i < order.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(order[i]).Append(" ×").Append(tally[order[i]]);
        }

        return sb.ToString();
    }
}
