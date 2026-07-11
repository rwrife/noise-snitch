using System;
using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Clamping/normalization rules for <see cref="Settings"/> (M5). These guard the
/// runtime against corrupt or hand-edited files: a bad value must never wedge the
/// timer or throw when constructing the event store.
/// </summary>
public sealed class SettingsTests
{
    [Fact]
    public void Defaults_Are_The_Documented_Constants()
    {
        var s = Settings.Defaults();
        Assert.Equal(Settings.DefaultPollIntervalMs, s.PollIntervalMs);
        Assert.Equal(Settings.DefaultEventsToKeep, s.EventsToKeep);
        Assert.Equal(Settings.DefaultPeakThreshold, s.PeakThreshold);
        Assert.Equal(Settings.DefaultReleaseMs, s.ReleaseMs);
    }

    [Fact]
    public void Persistence_Defaults_Off_With_Sane_Log_Cap()
    {
        // M6: durable logging must be opt-in (privacy/local-only), and the cap
        // must be a real, positive size out of the box.
        var s = Settings.Defaults();
        Assert.False(s.PersistLog);
        Assert.Equal(Settings.DefaultMaxLogBytes, s.MaxLogBytes);
        Assert.True(s.MaxLogBytes > 0);
    }

    [Fact]
    public void QuietHours_Defaults_Off_With_Sensible_Overnight_Window()
    {
        // Issue #8: escalation is opt-in, and the materialized defaults describe a
        // realistic overnight window (22:00 -> 07:00) so a user flipping it on has
        // a working example immediately.
        var s = Settings.Defaults();
        Assert.False(s.QuietHoursEnabled);
        Assert.Equal(Settings.DefaultQuietHoursStart, s.QuietHoursStart);
        Assert.Equal(Settings.DefaultQuietHoursEnd, s.QuietHoursEnd);
        Assert.Equal(22 * 60, s.QuietHoursStartMinute);
        Assert.Equal(7 * 60, s.QuietHoursEndMinute);
    }

    [Fact]
    public void QuietHours_Minute_Accessors_Parse_The_Strings()
    {
        var s = new Settings { QuietHoursStart = "23:15", QuietHoursEnd = "06:45" };
        Assert.Equal(23 * 60 + 15, s.QuietHoursStartMinute);
        Assert.Equal(6 * 60 + 45, s.QuietHoursEndMinute);
    }

    [Fact]
    public void QuietHours_Minute_Accessors_Fall_Back_On_Junk()
    {
        // Unparseable window strings resolve to the built-in defaults rather than
        // throwing or reading as 00:00 (which would silently change behaviour).
        var s = new Settings { QuietHoursStart = "nonsense", QuietHoursEnd = "" };
        Assert.Equal(22 * 60, s.QuietHoursStartMinute); // default 22:00
        Assert.Equal(7 * 60, s.QuietHoursEndMinute);    // default 07:00
    }

    [Fact]
    public void Normalized_Canonicalizes_QuietHours_Window_Strings()
    {
        // A sloppy "9:5" becomes "09:05"; junk snaps back to the default so the
        // persisted file always holds a well-formed HH:mm pair.
        var s = new Settings { QuietHoursStart = "9:5", QuietHoursEnd = "garbage" }.Normalized();
        Assert.Equal("09:05", s.QuietHoursStart);
        Assert.Equal(Settings.DefaultQuietHoursEnd, s.QuietHoursEnd);
    }

    [Fact]
    public void Normalized_Preserves_QuietHoursEnabled_Toggle()
    {
        Assert.True(new Settings { QuietHoursEnabled = true }.Normalized().QuietHoursEnabled);
        Assert.False(new Settings { QuietHoursEnabled = false }.Normalized().QuietHoursEnabled);
    }

    [Fact]
    public void Normalized_Clamps_MaxLogBytes_Into_Range()
    {
        // Non-positive -> default; below floor -> floor; above ceiling -> ceiling.
        Assert.Equal(Settings.DefaultMaxLogBytes, new Settings { MaxLogBytes = 0 }.Normalized().MaxLogBytes);
        Assert.Equal(Settings.DefaultMaxLogBytes, new Settings { MaxLogBytes = -1 }.Normalized().MaxLogBytes);
        Assert.Equal(Settings.MinMaxLogBytes, new Settings { MaxLogBytes = 1 }.Normalized().MaxLogBytes);
        Assert.Equal(Settings.MaxMaxLogBytes, new Settings { MaxLogBytes = long.MaxValue }.Normalized().MaxLogBytes);
    }

    [Fact]
    public void Normalized_Preserves_PersistLog_Toggle()
    {
        Assert.True(new Settings { PersistLog = true }.Normalized().PersistLog);
        Assert.False(new Settings { PersistLog = false }.Normalized().PersistLog);
    }

    [Fact]
    public void IgnoredApps_Defaults_Empty()
    {
        Assert.Empty(Settings.Defaults().IgnoredApps);
    }

    [Fact]
    public void Normalized_Canonicalizes_And_Dedupes_IgnoredApps()
    {
        var s = new Settings
        {
            IgnoredApps = new[] { "Spotify.exe", "spotify", "  ", "Chrome", "chrome" },
        }.Normalized();

        // Lower-cased, .exe stripped, blanks dropped, duplicates collapsed, sorted.
        Assert.Equal(new[] { "chrome", "spotify" }, s.IgnoredApps);
    }

    [Fact]
    public void Normalized_Passes_Through_Valid_Values()
    {
        var s = new Settings
        {
            PollIntervalMs = 500,
            EventsToKeep = 123,
            PeakThreshold = 0.25f,
            ReleaseMs = 800,
        }.Normalized();

        Assert.Equal(500, s.PollIntervalMs);
        Assert.Equal(123, s.EventsToKeep);
        Assert.Equal(0.25f, s.PeakThreshold);
        Assert.Equal(800, s.ReleaseMs);
    }

    [Fact]
    public void Normalized_Snaps_NonPositive_To_Defaults()
    {
        // Zero/negative are the classic corrupt-file footguns (spin the timer /
        // throw in EventStore). Any non-positive value snaps to the default.
        var s = new Settings
        {
            PollIntervalMs = 0,
            EventsToKeep = -5,
            ReleaseMs = -1,
        }.Normalized();

        Assert.Equal(Settings.DefaultPollIntervalMs, s.PollIntervalMs);
        Assert.Equal(Settings.DefaultEventsToKeep, s.EventsToKeep);
        // A negative release is corrupt, so it snaps to the default (not the 0
        // floor). Zero itself is a valid "no debounce" choice and is preserved
        // (see Normalized_Zero_ReleaseMs_Is_Preserved).
        Assert.Equal(Settings.DefaultReleaseMs, s.ReleaseMs);
    }

    [Fact]
    public void Normalized_Zero_ReleaseMs_Is_Preserved()
    {
        // 0 = "fire on every onset, no debounce" is a legitimate choice.
        var s = new Settings { ReleaseMs = 0 }.Normalized();
        Assert.Equal(0, s.ReleaseMs);
    }

    [Fact]
    public void Normalized_Clamps_Above_Max()
    {
        var s = new Settings
        {
            PollIntervalMs = 5_000_000,
            EventsToKeep = 1_000_000,
            PeakThreshold = 42f,
            ReleaseMs = 5_000_000,
        }.Normalized();

        Assert.Equal(Settings.MaxPollIntervalMs, s.PollIntervalMs);
        Assert.Equal(Settings.MaxEventsToKeep, s.EventsToKeep);
        Assert.Equal(Settings.MaxPeakThreshold, s.PeakThreshold);
        Assert.Equal(Settings.MaxReleaseMs, s.ReleaseMs);
    }

    [Fact]
    public void Normalized_Clamps_Small_But_Positive_To_Floor()
    {
        var s = new Settings { PollIntervalMs = 1, EventsToKeep = 1 }.Normalized();
        Assert.Equal(Settings.MinPollIntervalMs, s.PollIntervalMs);
        Assert.Equal(Settings.MinEventsToKeep, s.EventsToKeep);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Normalized_Coerces_NonFinite_Peak_To_Default(float bad)
    {
        var s = new Settings { PeakThreshold = bad }.Normalized();
        Assert.Equal(Settings.DefaultPeakThreshold, s.PeakThreshold);
    }

    [Fact]
    public void Normalized_Clamps_Negative_Peak_To_Min()
    {
        var s = new Settings { PeakThreshold = -0.5f }.Normalized();
        Assert.Equal(Settings.MinPeakThreshold, s.PeakThreshold);
    }

    [Fact]
    public void Normalized_Does_Not_Mutate_Source()
    {
        var src = new Settings { PollIntervalMs = 0 };
        _ = src.Normalized();
        Assert.Equal(0, src.PollIntervalMs); // original untouched
    }

    [Fact]
    public void Normalized_EventsToKeep_Is_Valid_For_EventStore()
    {
        // The whole point: a normalized capacity must never throw in EventStore.
        var s = new Settings { EventsToKeep = 0 }.Normalized();
        var ex = Record.Exception(() =>
        {
            _ = new NoiseSnitch.AudioWatcher.EventStore(s.EventsToKeep);
        });
        Assert.Null(ex);
    }
}
