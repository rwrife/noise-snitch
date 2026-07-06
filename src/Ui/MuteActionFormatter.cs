using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// Pure wording for the v0.2 "Mute-the-snitched" action (issue #7): the label of
/// the per-row context-menu item and the balloon-tip feedback after a mute /
/// unmute attempt.
///
/// Split out from the WinForms <c>BlotterForm</c> — like <see cref="BlotterFormatter"/>
/// and <see cref="FriendlyName"/> — so the phrasing is unit-testable without
/// spinning up any UI, and so the blotter, tray, and tests all agree on it.
/// </summary>
internal static class MuteActionFormatter
{
    /// <summary>
    /// The context-menu label for the mute toggle on a row, using the culprit's
    /// friendly name and its <em>current</em> mute state, e.g.
    /// <c>Mute Google Chrome</c> or <c>Unmute Google Chrome</c>. When
    /// <paramref name="currentlyMuted"/> is true the action would unmute.
    /// </summary>
    public static string ToggleLabel(uint processId, string? processName, bool currentlyMuted)
    {
        string who = FriendlyName.ForEvent(processId, processName);
        return currentlyMuted ? $"Unmute {who}" : $"Mute {who}";
    }

    /// <summary>
    /// Human feedback (tray balloon) describing what happened when the user hit
    /// the toggle, given the resolved <see cref="MuteOutcome"/> and the culprit.
    /// </summary>
    public static string Feedback(MuteOutcome outcome, uint processId, string? processName)
    {
        string who = FriendlyName.ForEvent(processId, processName);
        return outcome switch
        {
            MuteOutcome.Muted => $"Muted {who}.",
            MuteOutcome.Unmuted => $"Unmuted {who}.",
            MuteOutcome.NoSession => $"{who} isn't making sound right now — nothing to mute.",
            MuteOutcome.SystemSoundsDeclined => "System sounds can't be muted from here.",
            MuteOutcome.Failed => $"Couldn't change {who}'s mute state — try again.",
            _ => $"Couldn't change {who}'s mute state — try again.",
        };
    }

    /// <summary>
    /// Whether a mute toggle should even be offered for a given process. The
    /// system-sounds session (pid 0) is shared shell audio, not a single culprit
    /// app, so we deliberately don't offer to mute it.
    /// </summary>
    public static bool CanOfferToggle(uint processId) => processId != 0;
}
