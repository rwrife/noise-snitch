using System;

namespace NoiseSnitch.Model;

/// <summary>
/// A single "an app just started making sound" event — the unit the blotter
/// (M4) renders and the thing the rest of the app cares about.
///
/// Produced by the edge detector when a session crosses from silent → active
/// (see <see cref="NoiseSnitch.AudioWatcher.EdgeDetector"/>). Unlike a raw
/// <see cref="AudioSessionSnapshot"/> (one per session per tick), a
/// <see cref="NoiseEvent"/> is emitted at most once per onset, after debounce,
/// so a continuous stream of audio yields one event, not one per tick.
/// </summary>
/// <param name="TimestampUtc">When the onset was detected.</param>
/// <param name="ProcessId">Owning process id, or <c>0</c> for the system sounds session.</param>
/// <param name="ProcessName">Resolved process name (e.g. <c>chrome</c>) or best-effort fallback.</param>
/// <param name="Peak">Peak meter value (<c>[0, 1]</c>) at the moment the onset was detected.</param>
/// <param name="SessionName">Session display name as reported by Windows, when present.</param>
/// <param name="ExecutablePath">
/// Best-effort full path to the owning process's executable, used by the M5
/// blotter to render the app's icon. May be empty when the process has exited or
/// its path could not be read, in which case the UI falls back to a generic glyph.
/// </param>
internal readonly record struct NoiseEvent(
    DateTime TimestampUtc,
    uint ProcessId,
    string ProcessName,
    float Peak,
    string SessionName,
    string ExecutablePath = "")
{
    /// <summary>
    /// Builds a <see cref="NoiseEvent"/> from the snapshot that triggered the
    /// silent → active transition.
    /// </summary>
    public static NoiseEvent FromSnapshot(AudioSessionSnapshot s) => new(
        s.TimestampUtc,
        s.ProcessId,
        s.ProcessName,
        s.PeakValue,
        s.SessionName,
        s.ExecutablePath);

    /// <summary>
    /// A short, log-friendly one-liner, e.g.
    /// <c>chrome (pid 4821) just made noise (peak=0.42) session="Chrome"</c>.
    /// </summary>
    public override string ToString()
    {
        var name = string.IsNullOrWhiteSpace(SessionName) ? "" : $" session=\"{SessionName}\"";
        return $"{ProcessName} (pid {ProcessId}) just made noise (peak={Peak:0.000}){name}";
    }
}
