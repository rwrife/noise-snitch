using System;

namespace NoiseSnitch.Model;

/// <summary>
/// One app's slice of a <see cref="NoiseSnitch.Ui.DigestBuilder"/> digest: how
/// many <see cref="NoiseEvent"/>s it produced in the window and what share of
/// the total that represents (issue #23).
///
/// Pure data — no UI, no live-audio dependency — so it can be produced and
/// asserted on in unit tests. Aggregation/percentage math lives in
/// <see cref="NoiseSnitch.Ui.DigestBuilder"/>; text rendering lives in
/// <see cref="NoiseSnitch.Ui.DigestFormatter"/>.
/// </summary>
/// <param name="ProcessId">Owning process id, or <c>0</c> for the system-sounds session.</param>
/// <param name="ProcessName">Raw process name; the formatter friendly-maps it for display.</param>
/// <param name="Count">Number of noise events attributed to this app in the window.</param>
/// <param name="Percent">
/// This app's share of the window's total events, <c>[0, 100]</c>, rounded to
/// the nearest whole percent. Shares need not sum to exactly 100 after rounding.
/// </param>
internal readonly record struct DigestRow(
    uint ProcessId,
    string ProcessName,
    int Count,
    int Percent);
