using System;
using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// Pure formatting of a <see cref="NoiseEvent"/> into the one-line strings the
/// blotter (M4) shows. Split out from the WinForms <c>BlotterForm</c> so the
/// wording is unit-testable without spinning up any UI.
/// </summary>
internal static class BlotterFormatter
{
    /// <summary>Shown when the store has no events yet.</summary>
    public const string EmptyState = "All quiet… for now 🤫";

    /// <summary>
    /// The primary blotter line for an event, e.g. <c>3s ago — chrome</c>. Falls
    /// back to the pid when no process name was resolved so a row is never blank.
    /// </summary>
    public static string Line(NoiseEvent e, DateTime nowUtc)
    {
        string who = string.IsNullOrWhiteSpace(e.ProcessName)
            ? $"pid {e.ProcessId}"
            : e.ProcessName;

        return $"{RelativeTime.Format(e.TimestampUtc, nowUtc)} — {who}";
    }

    /// <summary>
    /// A secondary detail line (tooltip / sub-text): absolute local time, peak,
    /// and session name when present.
    /// </summary>
    public static string Detail(NoiseEvent e)
    {
        string when = e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
        string session = string.IsNullOrWhiteSpace(e.SessionName)
            ? string.Empty
            : $" · {e.SessionName}";
        return $"{when} · peak {e.Peak:0.00}{session}";
    }
}
