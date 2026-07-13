using System;
using System.Collections.Generic;
using System.Text;
using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// Pure text rendering of a ranked <see cref="Leaderboard"/> into the lines the
/// UI shows (issue #22). Split from any WinForms so the wording is unit-testable
/// without spinning up a window.
///
/// Each row reads like <c>1. 🥇 Slack — 38</c>: rank number, an optional medal
/// for the top three, the friendly app name (via <see cref="FriendlyName"/>),
/// and the event count. Rendering never re-sorts; it trusts the order handed to
/// it by <see cref="Leaderboard.Rank"/> / <see cref="Leaderboard.ForDay"/>.
/// </summary>
internal static class LeaderboardFormatter
{
    /// <summary>Shown when no app has made any noise in the window.</summary>
    public const string EmptyState = "Nobody's made a peep 🤫";

    /// <summary>Medals for the top three offenders, by 1-based rank.</summary>
    private static readonly Dictionary<int, string> Medals = new()
    {
        [1] = "🥇",
        [2] = "🥈",
        [3] = "🥉",
    };

    /// <summary>
    /// A single leaderboard line, e.g. <c>1. 🥇 Slack — 38</c>. When
    /// <paramref name="withMedals"/> is false, or the rank is outside the top
    /// three, no medal is shown and the line reads <c>4. Firefox — 9</c>.
    /// </summary>
    public static string Line(LeaderboardRow row, bool withMedals = true)
    {
        string who = FriendlyName.ForEvent(row.ProcessId, row.ProcessName);
        string medal = withMedals && Medals.TryGetValue(row.Rank, out var m) ? $"{m} " : string.Empty;
        return $"{row.Rank}. {medal}{who} — {row.Count}";
    }

    /// <summary>
    /// Renders the whole leaderboard as newline-joined lines, or the
    /// <see cref="EmptyState"/> when there are no rows.
    /// </summary>
    public static string Render(IReadOnlyList<LeaderboardRow> rows, bool withMedals = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return EmptyState;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(Line(rows[i], withMedals));
        }

        return sb.ToString();
    }
}
