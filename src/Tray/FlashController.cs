using System;

namespace NoiseSnitch.Tray;

/// <summary>
/// The pure, UI-free brain behind the M5 "tray icon flashes on each new event"
/// behaviour. It decides — given the time of the most recent onset and the
/// current time — whether the tray icon should currently be showing its
/// attention-grabbing <em>flash</em> state, and when that state is due to expire.
///
/// Splitting this out from the <see cref="NotifyIcon"/> plumbing keeps the timing
/// rules (how long a flash lasts, how bursts of events coalesce) pure and
/// unit-testable with a fixed clock — mirroring how <see cref="NoiseSnitch.Ui.RelativeTime"/>
/// and <see cref="NoiseSnitch.AudioWatcher.EdgeDetector"/> keep their logic free
/// of WinForms. The <see cref="TrayApplicationContext"/> owns the actual icon swap
/// and a WinForms timer; it just asks this type "should I be flashing right now?"
/// and "when should I check again?".
///
/// Behaviour:
/// <list type="bullet">
/// <item>A single onset lights the icon for <see cref="FlashDuration"/>, then it
/// falls back to the resting icon.</item>
/// <item>Bursts <b>coalesce</b>: each new onset restarts the window from that
/// moment, so a stream of events keeps the icon lit continuously (one steady
/// flash) instead of strobing — the goal is "glance and notice", not a seizure.</item>
/// <item>Clock skew is tolerated: a trigger timestamp in the future still lights
/// the icon and the window is measured from that timestamp.</item>
/// </list>
/// This type is not thread-safe; the tray drives it from the single UI thread.
/// </summary>
internal sealed class FlashController
{
    /// <summary>
    /// Default lit duration after an onset. Long enough to catch the eye when you
    /// glance at the tray, short enough that an idle machine settles back to its
    /// calm resting icon quickly.
    /// </summary>
    public static readonly TimeSpan DefaultFlashDuration = TimeSpan.FromMilliseconds(1200);

    /// <summary>How long the icon stays lit after the most recent onset.</summary>
    public TimeSpan FlashDuration { get; }

    // The instant the current flash window ends, or null when resting. Stored as
    // an absolute expiry so IsFlashing is a trivial comparison and coalescing is
    // "push the expiry out".
    private DateTime? _expiresAtUtc;

    /// <param name="flashDuration">
    /// How long to stay lit after each onset. Non-positive values fall back to
    /// <see cref="DefaultFlashDuration"/> so a bad setting can't disable the flash
    /// (or, worse, wedge a zero-length window that never clears).
    /// </param>
    public FlashController(TimeSpan? flashDuration = null)
    {
        TimeSpan d = flashDuration ?? DefaultFlashDuration;
        FlashDuration = d > TimeSpan.Zero ? d : DefaultFlashDuration;
    }

    /// <summary>
    /// Records an onset at <paramref name="nowUtc"/> and (re)starts the flash
    /// window from that instant. Returns <c>true</c> if this call caused a
    /// transition from resting → flashing (i.e. the icon needs to be swapped to
    /// the flash variant now); <c>false</c> if it merely extended an already-lit
    /// window. Callers use the return to avoid redundant icon assignments.
    /// </summary>
    public bool Trigger(DateTime nowUtc)
    {
        bool wasResting = !IsFlashing(nowUtc);
        _expiresAtUtc = nowUtc + FlashDuration;
        return wasResting;
    }

    /// <summary>
    /// Whether the icon should currently render its flash (lit) state at
    /// <paramref name="nowUtc"/>. Resting once the window has elapsed.
    /// </summary>
    public bool IsFlashing(DateTime nowUtc) =>
        _expiresAtUtc is { } expiry && nowUtc < expiry;

    /// <summary>
    /// The time remaining until the flash window elapses at
    /// <paramref name="nowUtc"/>, or <see cref="TimeSpan.Zero"/> if already
    /// resting. The tray schedules its restore timer for this interval; when it
    /// fires it re-checks <see cref="IsFlashing"/> (a later onset may have pushed
    /// the expiry out, in which case it reschedules).
    /// </summary>
    public TimeSpan RemainingUntil(DateTime nowUtc)
    {
        if (_expiresAtUtc is not { } expiry)
        {
            return TimeSpan.Zero;
        }

        TimeSpan remaining = expiry - nowUtc;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Forces the controller back to the resting state immediately (e.g. on
    /// shutdown, or after the tray has restored the calm icon). Idempotent.
    /// </summary>
    public void Reset() => _expiresAtUtc = null;
}
