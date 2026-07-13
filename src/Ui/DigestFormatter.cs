using System;
using System.Linq;
using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// Pure text rendering of a <see cref="NoiseDigest"/> into a compact, glanceable
/// summary (issue #23), e.g.
/// <c>214 sounds today — Slack 38%, chrome 22%, Zoom 15%</c>.
///
/// Split from any WinForms so the wording is unit-testable without spinning up a
/// window. Never re-sorts; it trusts the count-descending order handed to it by
/// <see cref="DigestBuilder"/>. App names are friendly-mapped via
/// <see cref="FriendlyName"/> to match the blotter and leaderboard.
/// </summary>
internal static class DigestFormatter
{
    /// <summary>Shown when nothing has made noise in the window.</summary>
    public const string EmptyState = "No noise snitched yet today 🤫";

    /// <summary>How many top offenders the one-line summary names before "…".</summary>
    private const int TopN = 5;

    /// <summary>
    /// A compact one-liner: the total followed by the top few offenders with
    /// their percentage shares. Falls back to <see cref="EmptyState"/> for an
    /// empty window. Uses "sound"/"sounds" pluralization for the count.
    /// </summary>
    public static string Render(NoiseDigest digest)
    {
        if (digest.Total == 0 || digest.Breakdown.Count == 0)
        {
            return EmptyState;
        }

        string noun = digest.Total == 1 ? "sound" : "sounds";
        var top = digest.Breakdown.Take(TopN)
            .Select(r => $"{FriendlyName.ForEvent(r.ProcessId, r.ProcessName)} {r.Percent}%");
        string offenders = string.Join(", ", top);
        string more = digest.Breakdown.Count > TopN ? ", …" : string.Empty;

        return $"{digest.Total} {noun} today — {offenders}{more}";
    }
}
