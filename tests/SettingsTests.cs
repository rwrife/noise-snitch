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
