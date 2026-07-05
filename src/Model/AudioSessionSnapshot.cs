using System;

namespace NoiseSnitch.Model;

/// <summary>
/// A point-in-time reading of a single Windows audio render session: which
/// process owns it, how loud it is right now, and the friendly name Windows
/// reports (when any).
///
/// M2 captures these on a timer and dumps them to the debug log to prove the
/// data flows. M3 will diff <see cref="PeakValue"/> across snapshots to detect
/// the silent → active edge and promote a transition into a <see cref="NoiseEvent"/>.
/// </summary>
/// <param name="TimestampUtc">When the reading was taken.</param>
/// <param name="ProcessId">Owning process id, or <c>0</c> for the system sounds session.</param>
/// <param name="ProcessName">
/// Resolved process name (e.g. <c>chrome</c>), or a best-effort fallback such as
/// <c>System Sounds</c> / <c>pid:1234 (exited)</c> when the process is gone.
/// </param>
/// <param name="SessionName">
/// The session's display name as reported by Windows, when present (often empty).
/// </param>
/// <param name="PeakValue">Current peak meter value in <c>[0, 1]</c>.</param>
/// <param name="IsActive">
/// True when the session is in the <c>AudioSessionStateActive</c> state.
/// </param>
/// <param name="ExecutablePath">
/// Best-effort full path to the owning process's executable
/// (e.g. <c>C:\Program Files\Google\Chrome\Application\chrome.exe</c>), used by
/// the M5 blotter to extract the app's icon. Often empty — the process may have
/// exited, be a system session (pid 0), or be inaccessible without elevation —
/// in which case the UI falls back to a generic glyph.
/// </param>
internal readonly record struct AudioSessionSnapshot(
    DateTime TimestampUtc,
    uint ProcessId,
    string ProcessName,
    string SessionName,
    float PeakValue,
    bool IsActive,
    string ExecutablePath = "")
{
    /// <summary>
    /// A short, log-friendly one-liner, e.g.
    /// <c>chrome (pid 4821) peak=0.42 active session="Chrome"</c>.
    /// </summary>
    public override string ToString()
    {
        var name = string.IsNullOrWhiteSpace(SessionName) ? "" : $" session=\"{SessionName}\"";
        var state = IsActive ? "active" : "idle";
        return $"{ProcessName} (pid {ProcessId}) peak={PeakValue:0.000} {state}{name}";
    }
}
