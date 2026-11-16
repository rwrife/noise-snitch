using NoiseSnitch.Model;
using NoiseSnitch.Personality;

namespace NoiseSnitch.Ui;

/// <summary>
/// Issue #29 "Notification-only mode": pure wording for the per-event Windows
/// toast raised when <see cref="NotificationMode.Toast"/> or
/// <see cref="NotificationMode.Both"/> is active.
///
/// Split out from the WinForms tray plumbing — like
/// <see cref="QuietHoursAlertFormatter"/> and <see cref="MuteActionFormatter"/> —
/// so the phrasing (and the fact that it runs through the active personality
/// pack) is unit-testable without any UI, and so the tray and tests agree on
/// exactly what the toast says.
/// </summary>
internal static class NotificationFormatter
{
    /// <summary>Balloon title for a per-event notification. Short, glanceable.</summary>
    public const string ToastTitle = "🔊 noise-snitch";

    /// <summary>
    /// Builds the toast body for a caught onset, naming the culprit via the same
    /// <see cref="FriendlyName"/> resolution the blotter uses, phrased through the
    /// active <paramref name="personality"/> pack, and tagged with a short
    /// relative time so a burst of toasts still reads clearly. Example (butler,
    /// just now): <c>I regret to inform you that Google Chrome broke the silence.
    /// (now)</c>.
    /// </summary>
    public static string Body(
        NoiseEvent e,
        SnitchPersonality personality,
        System.DateTime nowUtc)
    {
        string who = FriendlyName.ForEvent(e.ProcessId, e.ProcessName);
        string phrased = personality.PhraseEvent(who);
        string when = RelativeTime.Format(e.TimestampUtc, nowUtc);
        return $"{phrased} ({when})";
    }
}
