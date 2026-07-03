using System;
using NoiseSnitch.Tray;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Timing/coalescing rules of the pure <see cref="FlashController"/> — the brain
/// behind the M5 "tray icon flashes on each new event" behaviour. All tests pass
/// a fixed clock so the flash window is deterministic (no real timers involved).
/// </summary>
public sealed class FlashControllerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(1000);

    private static FlashController New() => new(Window);

    [Fact]
    public void Resting_Before_Any_Trigger()
    {
        var c = New();
        Assert.False(c.IsFlashing(T0));
        Assert.Equal(TimeSpan.Zero, c.RemainingUntil(T0));
    }

    [Fact]
    public void Trigger_Lights_The_Icon_For_The_Window()
    {
        var c = New();

        Assert.True(c.Trigger(T0)); // resting -> flashing transition
        Assert.True(c.IsFlashing(T0));
        Assert.True(c.IsFlashing(T0.AddMilliseconds(999)));
        // Exactly at expiry the window is closed (strict <).
        Assert.False(c.IsFlashing(T0.AddMilliseconds(1000)));
        Assert.False(c.IsFlashing(T0.AddMilliseconds(1001)));
    }

    [Fact]
    public void Trigger_Returns_False_When_Already_Flashing()
    {
        var c = New();

        Assert.True(c.Trigger(T0));                       // first: transition
        Assert.False(c.Trigger(T0.AddMilliseconds(200))); // still lit: extension only
    }

    [Fact]
    public void Bursts_Coalesce_By_Extending_The_Window()
    {
        var c = New();
        c.Trigger(T0);
        // A second onset 800ms in pushes expiry to 800+1000 = 1800ms.
        c.Trigger(T0.AddMilliseconds(800));

        // Would have expired at 1000ms under a single trigger; still lit now.
        Assert.True(c.IsFlashing(T0.AddMilliseconds(1500)));
        Assert.True(c.IsFlashing(T0.AddMilliseconds(1799)));
        Assert.False(c.IsFlashing(T0.AddMilliseconds(1800)));
    }

    [Fact]
    public void RemainingUntil_Counts_Down_And_Floors_At_Zero()
    {
        var c = New();
        c.Trigger(T0);

        Assert.Equal(Window, c.RemainingUntil(T0));
        Assert.Equal(TimeSpan.FromMilliseconds(400), c.RemainingUntil(T0.AddMilliseconds(600)));
        // Past expiry never goes negative.
        Assert.Equal(TimeSpan.Zero, c.RemainingUntil(T0.AddMilliseconds(2000)));
    }

    [Fact]
    public void Reset_Returns_To_Resting()
    {
        var c = New();
        c.Trigger(T0);
        Assert.True(c.IsFlashing(T0.AddMilliseconds(100)));

        c.Reset();
        Assert.False(c.IsFlashing(T0.AddMilliseconds(100)));
        Assert.Equal(TimeSpan.Zero, c.RemainingUntil(T0.AddMilliseconds(100)));

        // A fresh trigger after reset behaves as a new resting->flashing transition.
        Assert.True(c.Trigger(T0.AddMilliseconds(100)));
    }

    [Fact]
    public void Retrigger_After_Expiry_Is_A_New_Transition()
    {
        var c = New();
        c.Trigger(T0);

        // Well past the window: back to resting, so the next onset transitions again.
        DateTime later = T0.AddMilliseconds(5000);
        Assert.False(c.IsFlashing(later));
        Assert.True(c.Trigger(later));
        Assert.True(c.IsFlashing(later));
    }

    [Fact]
    public void Future_Timestamp_Is_Tolerated()
    {
        var c = New();
        // Clock skew: an onset stamped slightly in the future still lights the
        // icon, with the window measured from that timestamp.
        DateTime future = T0.AddMilliseconds(500);
        Assert.True(c.Trigger(future));
        Assert.True(c.IsFlashing(future));
        Assert.True(c.IsFlashing(future.AddMilliseconds(999)));
        Assert.False(c.IsFlashing(future.AddMilliseconds(1000)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void NonPositive_Duration_Falls_Back_To_Default(int ms)
    {
        var c = new FlashController(TimeSpan.FromMilliseconds(ms));
        Assert.Equal(FlashController.DefaultFlashDuration, c.FlashDuration);

        // And it actually flashes for that default window rather than never.
        c.Trigger(T0);
        Assert.True(c.IsFlashing(T0));
        Assert.True(c.IsFlashing(T0 + FlashController.DefaultFlashDuration - TimeSpan.FromMilliseconds(1)));
        Assert.False(c.IsFlashing(T0 + FlashController.DefaultFlashDuration));
    }

    [Fact]
    public void Default_Duration_Is_Used_When_Unspecified()
    {
        var c = new FlashController();
        Assert.Equal(FlashController.DefaultFlashDuration, c.FlashDuration);
    }
}
