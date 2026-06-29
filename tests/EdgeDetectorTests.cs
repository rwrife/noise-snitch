using System;
using System.Collections.Generic;
using System.Linq;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Edge + debounce behaviour of <see cref="EdgeDetector"/>, exercised entirely
/// over fake <see cref="AudioSessionSnapshot"/>s with controlled timestamps — no
/// WASAPI, no clock, fully deterministic.
/// </summary>
public sealed class EdgeDetectorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly EdgeDetectorOptions Opts = new(
        PeakThreshold: 0.10f,
        ReleaseTime: TimeSpan.FromMilliseconds(1000));

    /// <summary>Builds a one-session tick at <paramref name="t"/>.</summary>
    private static IReadOnlyList<AudioSessionSnapshot> Tick(
        DateTime t,
        float peak,
        bool active = true,
        uint pid = 4821,
        string process = "chrome",
        string session = "Chrome") =>
        new[]
        {
            new AudioSessionSnapshot(t, pid, process, session, peak, active),
        };

    private static DateTime Ms(int ms) => T0.AddMilliseconds(ms);

    [Fact]
    public void Onset_Fires_Once_On_Silent_To_Active()
    {
        var det = new EdgeDetector(Opts);

        var first = det.Process(Tick(Ms(0), peak: 0.5f));
        Assert.Single(first);
        Assert.Equal("chrome", first[0].ProcessName);
        Assert.Equal(0.5f, first[0].Peak);
        Assert.Equal(Ms(0), first[0].TimestampUtc);
    }

    [Fact]
    public void Starts_Silent_Then_Goes_Active_Fires_On_The_Active_Tick()
    {
        var det = new EdgeDetector(Opts);

        // Below threshold: no event.
        Assert.Empty(det.Process(Tick(Ms(0), peak: 0.01f)));
        // Crosses threshold: one event.
        var ev = det.Process(Tick(Ms(750), peak: 0.4f));
        Assert.Single(ev);
        Assert.Equal(Ms(750), ev[0].TimestampUtc);
    }

    [Fact]
    public void Continuous_Stream_Debounces_To_A_Single_Event()
    {
        var det = new EdgeDetector(Opts);

        var total = new List<NoiseEvent>();
        // 20 consecutive loud ticks 750ms apart — a continuous stream.
        for (int i = 0; i < 20; i++)
        {
            total.AddRange(det.Process(Tick(Ms(i * 750), peak: 0.6f)));
        }

        Assert.Single(total); // exactly one onset, not twenty
    }

    [Fact]
    public void Brief_Dip_Shorter_Than_Release_Does_Not_Refire()
    {
        var det = new EdgeDetector(Opts);

        Assert.Single(det.Process(Tick(Ms(0), peak: 0.6f)));   // onset
        Assert.Empty(det.Process(Tick(Ms(750), peak: 0.6f)));  // still sounding -> none

        // A single quiet tick (750ms < 1000ms release), then loud again.
        Assert.Empty(det.Process(Tick(Ms(1500), peak: 0.0f)));     // dip starts at 1500
        var afterDip = det.Process(Tick(Ms(2250), peak: 0.6f));    // only 750ms quiet
        Assert.Empty(afterDip);                                    // not re-armed yet
    }

    [Fact]
    public void Silence_Past_Release_Then_Sound_Fires_Again()
    {
        var det = new EdgeDetector(Opts);

        Assert.Single(det.Process(Tick(Ms(0), peak: 0.6f)));   // onset #1

        // Go quiet long enough to re-arm (>= 1000ms of silence).
        Assert.Empty(det.Process(Tick(Ms(750), peak: 0.0f)));  // quiet starts at 750
        Assert.Empty(det.Process(Tick(Ms(1500), peak: 0.0f))); // still quiet
        // 2000ms is >= 1000ms after silence began (750) -> re-armed.
        var again = det.Process(Tick(Ms(2000), peak: 0.6f));
        Assert.Single(again); // onset #2
    }

    [Fact]
    public void Vanished_Session_Then_Reappears_Loud_Refires_After_Release()
    {
        var det = new EdgeDetector(Opts);

        Assert.Single(det.Process(Tick(Ms(0), peak: 0.6f))); // onset

        // Session disappears entirely for several ticks (empty snapshots).
        Assert.Empty(det.Process(Array.Empty<AudioSessionSnapshot>()));
        Assert.Empty(det.Process(Array.Empty<AudioSessionSnapshot>()));

        // Reappears loud well after the release window.
        var back = det.Process(Tick(Ms(5000), peak: 0.6f));
        Assert.Single(back);
    }

    [Fact]
    public void Active_But_Below_Threshold_Never_Fires()
    {
        var det = new EdgeDetector(Opts);

        for (int i = 0; i < 10; i++)
        {
            Assert.Empty(det.Process(Tick(Ms(i * 750), peak: 0.05f, active: true)));
        }
    }

    [Fact]
    public void Inactive_State_Never_Fires_Even_With_High_Peak()
    {
        var det = new EdgeDetector(Opts);

        // Defensive: a stale/inactive session reporting a high peak must not snitch.
        Assert.Empty(det.Process(Tick(Ms(0), peak: 0.9f, active: false)));
    }

    [Fact]
    public void Distinct_Sessions_Are_Tracked_Independently()
    {
        var det = new EdgeDetector(Opts);

        var tick = new[]
        {
            new AudioSessionSnapshot(Ms(0), 100, "chrome", "Chrome", 0.6f, true),
            new AudioSessionSnapshot(Ms(0), 200, "slack", "Slack", 0.6f, true),
        };

        var events = det.Process(tick);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.ProcessName == "chrome");
        Assert.Contains(events, e => e.ProcessName == "slack");
    }

    [Fact]
    public void Same_Pid_Different_Session_Names_Fire_Separately()
    {
        var det = new EdgeDetector(Opts);

        var tick = new[]
        {
            new AudioSessionSnapshot(Ms(0), 100, "chrome", "Tab A", 0.6f, true),
            new AudioSessionSnapshot(Ms(0), 100, "chrome", "Tab B", 0.6f, true),
        };

        Assert.Equal(2, det.Process(tick).Count);
    }

    [Fact]
    public void Process_Rejects_Null_Snapshots()
    {
        var det = new EdgeDetector(Opts);
        Assert.Throws<ArgumentNullException>(() => det.Process(null!));
    }
}
