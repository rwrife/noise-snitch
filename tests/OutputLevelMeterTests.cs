using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

public sealed class OutputLevelMeterTests
{
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0.42f, 0.42f)]
    [InlineData(2f, 1f)]
    public void Clamps_Peaks_To_Normalized_Range(float peak, float expected)
    {
        Assert.Equal(expected, OutputLevelMeter.Update(0, peak, TimeSpan.Zero));
    }

    [Fact]
    public void Rises_Immediately_And_Decays_By_Elapsed_Time_Without_Undershooting_Input()
    {
        float level = OutputLevelMeter.Update(0.1f, 0.9f, TimeSpan.FromSeconds(1));
        Assert.Equal(0.9f, level);
        level = OutputLevelMeter.Update(level, 0.2f, TimeSpan.FromSeconds(0.5));
        Assert.Equal(0.65f, level, 5);
        level = OutputLevelMeter.Update(level, 0.2f, TimeSpan.FromSeconds(1));
        Assert.Equal(0.2f, level);
        Assert.Equal(0, OutputLevelMeter.Update(level, 0, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Unavailable_Or_Invalid_Peak_Clears_Held_Level(float? peak)
    {
        Assert.Equal(0, OutputLevelMeter.Update(1, peak, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(float.NaN, 0f)]
    public void Clamps_Held_Level(float previous, float expected)
    {
        Assert.Equal(expected, OutputLevelMeter.Update(previous, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Rejects_Negative_Elapsed_Time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OutputLevelMeter.Update(1, 0, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Poll_Publishes_Output_Even_Without_Onsets_And_Clears_It_On_Device_Loss()
    {
        float? peak = 0.8f;
        using var watcher = new SessionWatcher(
            snapshot: () => Array.Empty<AudioSessionSnapshot>(),
            outputPeak: () => peak);
        var levels = new List<float>();
        watcher.OutputLevelChanged += levels.Add;

        watcher.Poll();
        Assert.Equal(0.8f, watcher.OutputLevel);

        peak = null;
        watcher.Poll();

        Assert.Equal(new[] { 0.8f, 0f }, levels);
        Assert.Equal(0, watcher.OutputLevel);
        Assert.Empty(watcher.Events.Recent());

        peak = 1.5f;
        watcher.Poll();
        Assert.Equal(1, watcher.OutputLevel);
        Assert.Equal(new[] { 0.8f, 0f, 1f }, levels);
    }

    [Fact]
    public void Output_Level_Is_Independent_Of_Per_App_Ignore_Filter()
    {
        using var watcher = new SessionWatcher(
            ignore: new IgnoreList(new[] { "spotify" }),
            snapshot: () => new[]
            {
                new AudioSessionSnapshot(DateTime.UtcNow, 10, "spotify", "", 0.6f, true),
                new AudioSessionSnapshot(DateTime.UtcNow, 20, "chrome", "", 0.3f, true),
            },
            outputPeak: () => 0.8f);

        watcher.Poll();

        Assert.Equal(0.8f, watcher.OutputLevel);
        Assert.Equal("chrome", Assert.Single(watcher.Events.Recent()).ProcessName);
    }
}
