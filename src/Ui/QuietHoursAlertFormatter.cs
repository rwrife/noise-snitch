using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// Pure wording for the v0.2 "Quiet-hours alerting" escalation (issue #8): the
/// title and body of the tray balloon shown when an app makes a sound
/// <em>during</em> the user's configured quiet window.
///
/// Split out from the WinForms tray plumbing \u2014 like <see cref="MuteActionFormatter"/>
/// and <see cref="BlotterFormatter"/> \u2014 so the phrasing (and the friendly-name
/// resolution it shares with the blotter) is unit-testable without any UI, and so
/// the tray and tests agree on exactly what the alert says.
/// </summary>
internal static class QuietHoursAlertFormatter
{
    /// <summary>Balloon title for a quiet-hours escalation. Short, unmistakable.</summary>
    public const string AlertTitle = "🔊 Noise during quiet hours";

    /// <summary>
    /// The balloon body naming the culprit, e.g.
    /// <c>Google Chrome just made a sound during your quiet hours.</c>. Uses the
    /// same <see cref="FriendlyName"/> resolution as the blotter so the name shown
    /// in the toast matches the row.
    /// </summary>
    public static string Body(uint processId, string? processName)
    {
        string who = FriendlyName.ForEvent(processId, processName);
        return $"{who} just made a sound during your quiet hours.";
    }

    /// <summary>Convenience overload building the body straight from a <see cref="NoiseEvent"/>.</summary>
    public static string Body(NoiseEvent e) => Body(e.ProcessId, e.ProcessName);
}
