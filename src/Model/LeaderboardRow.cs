using System;

namespace NoiseSnitch.Model;

/// <summary>
/// One ranked row in the noise <see cref="NoiseSnitch.Ui.Leaderboard"/>: an app
/// (identified by its resolved <see cref="ProcessId"/>/<see cref="ProcessName"/>)
/// and how many <see cref="NoiseEvent"/>s it produced within the aggregation
/// window.
///
/// Pure data — no UI, no live-audio dependency — so it can be produced and
/// asserted on in unit tests. Ranking/tie-breaking lives in
/// <see cref="NoiseSnitch.Ui.Leaderboard"/>; text rendering lives in
/// <see cref="NoiseSnitch.Ui.LeaderboardFormatter"/>.
/// </summary>
/// <param name="Rank">1-based position after ranking (1 = loudest offender).</param>
/// <param name="ProcessId">Owning process id, or <c>0</c> for the system-sounds session.</param>
/// <param name="ProcessName">Raw process name; the formatter friendly-maps it for display.</param>
/// <param name="Count">Number of noise events attributed to this app in the window.</param>
internal readonly record struct LeaderboardRow(
    int Rank,
    uint ProcessId,
    string ProcessName,
    int Count);
