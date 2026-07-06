using System;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Membership rules of the pure <see cref="QuietHoursSchedule"/> \u2014 the brain
/// behind the v0.2 "Quiet-hours alerting" feature (issue #8). Every test passes a
/// fixed local <see cref="DateTime"/> so the window decision is deterministic (no
/// real clock). The window is <c>[start, end)</c> on the wall clock, with an
/// overnight window wrapping past midnight when start &gt; end.
/// </summary>
public sealed class QuietHoursScheduleTests
{
    // A fixed calendar date; only the time-of-day matters to the schedule.
    private static DateTime At(int hour, int minute) =>
        new(2026, 3, 15, hour, minute, 0, DateTimeKind.Local);

    private static int Hm(int hour, int minute) => (hour * 60) + minute;

    [Fact]
    public void Disabled_Is_Never_Quiet_Even_Inside_Window()
    {
        var s = new QuietHoursSchedule(enabled: false, Hm(22, 0), Hm(7, 0));
        Assert.False(s.IsQuietAt(At(23, 0))); // squarely inside a 22:00\u201307:00 window
        Assert.False(s.IsQuietAt(At(3, 0)));
    }

    [Fact]
    public void SameDay_Window_Is_Inclusive_Start_Exclusive_End()
    {
        // 09:00 \u2192 17:00 (a "work hours" style same-day window).
        var s = new QuietHoursSchedule(enabled: true, Hm(9, 0), Hm(17, 0));

        Assert.False(s.IsQuietAt(At(8, 59))); // before start
        Assert.True(s.IsQuietAt(At(9, 0)));   // start is inclusive
        Assert.True(s.IsQuietAt(At(12, 30))); // middle
        Assert.True(s.IsQuietAt(At(16, 59))); // just before end
        Assert.False(s.IsQuietAt(At(17, 0))); // end is exclusive
        Assert.False(s.IsQuietAt(At(17, 1))); // after end
    }

    [Fact]
    public void Overnight_Window_Wraps_Past_Midnight()
    {
        // 22:00 \u2192 07:00: the classic "don't wake me" window.
        var s = new QuietHoursSchedule(enabled: true, Hm(22, 0), Hm(7, 0));

        // Evening side (>= start).
        Assert.False(s.IsQuietAt(At(21, 59)));
        Assert.True(s.IsQuietAt(At(22, 0)));   // inclusive start
        Assert.True(s.IsQuietAt(At(23, 30)));
        // Across midnight.
        Assert.True(s.IsQuietAt(At(0, 0)));
        Assert.True(s.IsQuietAt(At(3, 15)));
        Assert.True(s.IsQuietAt(At(6, 59)));   // just before end
        // Morning boundary (exclusive end) and the wide-awake daytime gap.
        Assert.False(s.IsQuietAt(At(7, 0)));   // exclusive end
        Assert.False(s.IsQuietAt(At(12, 0)));
    }

    [Fact]
    public void Empty_Window_Start_Equals_End_Is_Never_Quiet()
    {
        // Coinciding endpoints mean "no window configured", not "all day".
        var s = new QuietHoursSchedule(enabled: true, Hm(10, 0), Hm(10, 0));
        Assert.True(s.IsEmptyWindow);
        Assert.False(s.IsQuietAt(At(10, 0)));
        Assert.False(s.IsQuietAt(At(10, 1)));
        Assert.False(s.IsQuietAt(At(22, 0)));
    }

    [Fact]
    public void Near_AllDay_Window_Covers_Almost_Everything()
    {
        // 00:00 \u2192 23:59 is the way to ask for "basically all day" (distinct endpoints).
        var s = new QuietHoursSchedule(enabled: true, Hm(0, 0), Hm(23, 59));
        Assert.True(s.IsQuietAt(At(0, 0)));
        Assert.True(s.IsQuietAt(At(12, 0)));
        Assert.True(s.IsQuietAt(At(23, 58)));
        Assert.False(s.IsQuietAt(At(23, 59))); // exclusive end \u2014 the one dead minute
    }

    [Fact]
    public void Constructor_Wraps_OutOfRange_Minutes_Defensively()
    {
        // 1500 minutes = 25:00 \u2192 wraps to 01:00; -60 \u2192 23:00.
        var s = new QuietHoursSchedule(enabled: true, startMinuteOfDay: 1500, endMinuteOfDay: -60);
        Assert.Equal(Hm(1, 0), s.StartMinuteOfDay);
        Assert.Equal(Hm(23, 0), s.EndMinuteOfDay);
    }

    [Fact]
    public void Seconds_Within_A_Minute_Do_Not_Change_Membership()
    {
        // The window is minute-granular: 06:59:59 is still before an 07:00 end.
        var s = new QuietHoursSchedule(enabled: true, Hm(22, 0), Hm(7, 0));
        var justBeforeEnd = new DateTime(2026, 3, 15, 6, 59, 59, DateTimeKind.Local);
        var atEnd = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Local);
        Assert.True(s.IsQuietAt(justBeforeEnd));
        Assert.False(s.IsQuietAt(atEnd));
    }

    [Fact]
    public void FromSettings_Honours_Enabled_And_Window_Strings()
    {
        var settings = new Settings
        {
            QuietHoursEnabled = true,
            QuietHoursStart = "23:00",
            QuietHoursEnd = "06:30",
        };

        var s = QuietHoursSchedule.FromSettings(settings);
        Assert.True(s.Enabled);
        Assert.Equal(Hm(23, 0), s.StartMinuteOfDay);
        Assert.Equal(Hm(6, 30), s.EndMinuteOfDay);
        Assert.True(s.IsQuietAt(At(2, 0)));    // overnight, inside
        Assert.False(s.IsQuietAt(At(9, 0)));   // daytime, outside
    }

    [Fact]
    public void FromSettings_Disabled_Never_Quiet()
    {
        var settings = new Settings { QuietHoursEnabled = false };
        var s = QuietHoursSchedule.FromSettings(settings);
        Assert.False(s.Enabled);
        Assert.False(s.IsQuietAt(At(23, 0)));
    }
}
