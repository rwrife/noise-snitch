using System;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// The pure, UI-free brain behind the v0.2 "Quiet-hours alerting" feature
/// (issue #8): given a user-defined focus window expressed as two wall-clock
/// times of day, it decides whether a given instant falls <em>inside</em> that
/// window — i.e. whether a fresh noise onset should be escalated (a loud toast)
/// rather than merely flashed.
///
/// Like <see cref="NoiseSnitch.Tray.FlashController"/> and
/// <see cref="NoiseSnitch.Ui.RelativeTime"/>, all timing logic lives here (free
/// of WinForms and WASAPI) so the window rules — inclusive start, exclusive end,
/// and the all-important <b>midnight wrap</b> (e.g. 22:00 → 07:00) — are pure and
/// deterministically unit-testable with a fixed clock. The tray owns the actual
/// toast; it just asks this type "are we in quiet hours right now?".
///
/// Times are compared against the <em>local</em> wall clock, because a "quiet
/// window" is inherently a human-schedule concept ("don't let anything wake me
/// between 10pm and 7am"): the caller passes a local <see cref="DateTime"/> and
/// this type reduces it to a minute-of-day. This type is immutable and
/// thread-safe.
/// </summary>
internal sealed class QuietHoursSchedule
{
    /// <summary>Minutes in a day. A minute-of-day is in <c>[0, 1440)</c>.</summary>
    public const int MinutesPerDay = 24 * 60;

    private readonly bool _enabled;
    private readonly int _startMinuteOfDay;
    private readonly int _endMinuteOfDay;

    /// <param name="enabled">
    /// When <c>false</c>, <see cref="IsQuietAt"/> is always <c>false</c> — the
    /// feature is off and nothing is ever escalated, regardless of the window.
    /// </param>
    /// <param name="startMinuteOfDay">
    /// Inclusive window start as a minute-of-day (<c>0</c> = 00:00). Clamped into
    /// <c>[0, 1440)</c> defensively so a bad value can't throw.
    /// </param>
    /// <param name="endMinuteOfDay">
    /// Exclusive window end as a minute-of-day. Clamped into <c>[0, 1440)</c>.
    /// </param>
    public QuietHoursSchedule(bool enabled, int startMinuteOfDay, int endMinuteOfDay)
    {
        _enabled = enabled;
        _startMinuteOfDay = Wrap(startMinuteOfDay);
        _endMinuteOfDay = Wrap(endMinuteOfDay);
    }

    /// <summary>Whether the quiet-hours feature is switched on.</summary>
    public bool Enabled => _enabled;

    /// <summary>Inclusive window start, as a normalized minute-of-day.</summary>
    public int StartMinuteOfDay => _startMinuteOfDay;

    /// <summary>Exclusive window end, as a normalized minute-of-day.</summary>
    public int EndMinuteOfDay => _endMinuteOfDay;

    /// <summary>
    /// A schedule whose start and end coincide. Treated as an <b>empty</b> window
    /// (never quiet) rather than an all-day one: "10:00 to 10:00" almost certainly
    /// means "no window configured yet", and defaulting to always-escalate would
    /// be a nasty surprise. Callers wanting all-day should use distinct times
    /// (e.g. 00:00 → 23:59).
    /// </summary>
    public bool IsEmptyWindow => _startMinuteOfDay == _endMinuteOfDay;

    /// <summary>
    /// Builds a schedule directly from the persisted <see cref="NoiseSnitch.Model.Settings"/>,
    /// which store the window as already-clamped minute-of-day integers.
    /// </summary>
    public static QuietHoursSchedule FromSettings(NoiseSnitch.Model.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new QuietHoursSchedule(
            settings.QuietHoursEnabled,
            settings.QuietHoursStartMinute,
            settings.QuietHoursEndMinute);
    }

    /// <summary>
    /// Whether <paramref name="localNow"/> falls inside the quiet window. The
    /// start is inclusive and the end exclusive, so a window is
    /// <c>[start, end)</c> on the wall clock. When the window wraps past midnight
    /// (<c>start &gt; end</c>, e.g. 22:00 → 07:00) an instant is quiet if it is at
    /// or after the start <em>or</em> before the end. Always <c>false</c> when the
    /// feature is disabled or the window is empty.
    /// </summary>
    /// <param name="localNow">
    /// The current <em>local</em> wall-clock time. Only its time-of-day matters;
    /// the date component is ignored.
    /// </param>
    public bool IsQuietAt(DateTime localNow)
    {
        if (!_enabled || IsEmptyWindow)
        {
            return false;
        }

        int minute = MinuteOfDay(localNow);

        // Same-day window, e.g. 09:00 → 17:00: quiet strictly within [start, end).
        if (_startMinuteOfDay < _endMinuteOfDay)
        {
            return minute >= _startMinuteOfDay && minute < _endMinuteOfDay;
        }

        // Wrapped (overnight) window, e.g. 22:00 → 07:00: quiet from start to
        // midnight, then midnight to end.
        return minute >= _startMinuteOfDay || minute < _endMinuteOfDay;
    }

    /// <summary>The time-of-day of <paramref name="local"/> as a minute-of-day in <c>[0, 1440)</c>.</summary>
    private static int MinuteOfDay(DateTime local)
    {
        // TimeOfDay is always in [0, 24h); integer minutes floors sub-minute parts,
        // matching the minute granularity the window is configured in.
        return (int)local.TimeOfDay.TotalMinutes;
    }

    /// <summary>Coerces any integer into a valid minute-of-day via modulo, defensively.</summary>
    private static int Wrap(int minuteOfDay)
    {
        int m = minuteOfDay % MinutesPerDay;
        return m < 0 ? m + MinutesPerDay : m;
    }
}
