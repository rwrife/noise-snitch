namespace NoiseSnitch.Model;

/// <summary>
/// The result of asking the app to mute or unmute the session behind a blotter
/// entry (the v0.2 "Mute-the-snitched" feature, issue #7).
///
/// Kept as a small, WinForms-free enum so the tray/blotter can map an outcome to
/// UI (row state, balloon text) without the muting service needing to know about
/// any of that, and so the mapping is unit-testable.
/// </summary>
internal enum MuteOutcome
{
    /// <summary>The app's session is now muted (we set, or found it already, muted).</summary>
    Muted,

    /// <summary>The app's session is now unmuted (we set, or found it already, unmuted).</summary>
    Unmuted,

    /// <summary>
    /// No live audio session was found for that process — it has since gone quiet
    /// or exited, so there was nothing to (un)mute. Not an error, just stale.
    /// </summary>
    NoSession,

    /// <summary>
    /// The system-sounds session (pid 0) was targeted. Windows exposes it, but
    /// muting it is intentionally not offered — it's shared shell audio, not a
    /// single culprit app — so the action is declined rather than attempted.
    /// </summary>
    SystemSoundsDeclined,

    /// <summary>
    /// A live session was found but toggling its mute state failed (a COM/WASAPI
    /// error, access denied, or the session vanished mid-operation). Non-fatal;
    /// the app just reports it couldn't do it.
    /// </summary>
    Failed,
}
