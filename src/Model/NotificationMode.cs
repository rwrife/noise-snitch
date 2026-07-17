namespace NoiseSnitch.Model;

/// <summary>
/// Issue #29: how the snitch surfaces a caught onset to the user.
///
/// The default (<see cref="Flash"/>) preserves the M5 behaviour: the tray icon
/// briefly lights up. <see cref="Toast"/> raises a Windows balloon/toast per
/// event (phrased by the active personality pack) <em>instead of</em> the flash,
/// and <see cref="Both"/> does both. Persisted via <c>Settings.NotificationMode</c>
/// and switchable live from the tray.
///
/// Kept as a plain enum so <c>System.Text.Json</c> round-trips it by name; an
/// unknown/missing value in the file falls back to <see cref="Flash"/> during
/// <see cref="Settings.Normalized"/>.
/// </summary>
internal enum NotificationMode
{
    /// <summary>M5 default: flash the tray icon only.</summary>
    Flash = 0,

    /// <summary>Raise a per-event Windows toast only (no icon flash).</summary>
    Toast = 1,

    /// <summary>Both flash the icon and raise a per-event toast.</summary>
    Both = 2,
}
