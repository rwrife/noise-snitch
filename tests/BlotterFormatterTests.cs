using System;
using NoiseSnitch.Model;
using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Pure formatting rules behind the M4 blotter rows: relative timestamps and the
/// "<i>when</i> — <i>who</i>" line. No WinForms is touched.
/// </summary>
public sealed class BlotterFormatterTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);

    private static NoiseEvent Event(DateTime tsUtc, string proc, uint pid = 100, float peak = 0.5f, string session = "") =>
        new(tsUtc, pid, proc, peak, session);

    [Theory]
    [InlineData(0, "now")]        // same instant
    [InlineData(0.4, "now")]      // sub-second rounds to now
    [InlineData(1, "1s ago")]
    [InlineData(3, "3s ago")]
    [InlineData(59, "59s ago")]
    [InlineData(60, "1m ago")]
    [InlineData(90, "1m ago")]    // truncates, not rounds
    [InlineData(3599, "59m ago")]
    [InlineData(3600, "1h ago")]
    [InlineData(7200, "2h ago")]
    [InlineData(86399, "23h ago")]
    [InlineData(86400, "1d ago")]
    [InlineData(172800, "2d ago")]
    public void Format_Buckets_By_Magnitude(double secondsAgo, string expected)
    {
        DateTime ts = Now.AddSeconds(-secondsAgo);
        Assert.Equal(expected, RelativeTime.Format(ts, Now));
    }

    [Fact]
    public void Format_Future_Timestamp_Reads_As_Now()
    {
        // Clock skew / an event stamped slightly ahead must not show a negative.
        DateTime future = Now.AddSeconds(5);
        Assert.Equal("now", RelativeTime.Format(future, Now));
    }

    [Fact]
    public void Line_Combines_RelativeTime_And_ProcessName()
    {
        var e = Event(Now.AddSeconds(-3), "chrome");
        Assert.Equal("3s ago — chrome", BlotterFormatter.Line(e, Now));
    }

    [Fact]
    public void Line_Falls_Back_To_Pid_When_Name_Missing()
    {
        var e = Event(Now.AddSeconds(-10), proc: "   ", pid: 4821);
        Assert.Equal("10s ago — pid 4821", BlotterFormatter.Line(e, Now));
    }

    [Fact]
    public void Detail_Includes_Peak_And_Session_When_Present()
    {
        var e = Event(Now, "slack", peak: 0.42f, session: "Slack Call");
        string detail = BlotterFormatter.Detail(e);
        Assert.Contains("peak 0.42", detail);
        Assert.Contains("Slack Call", detail);
    }

    [Fact]
    public void Detail_Omits_Session_Separator_When_Blank()
    {
        var e = Event(Now, "explorer", peak: 0.10f, session: "");
        string detail = BlotterFormatter.Detail(e);
        Assert.Contains("peak 0.10", detail);
        Assert.DoesNotContain(" · explorer", detail); // no trailing session chunk
        Assert.EndsWith("peak 0.10", detail);
    }

    [Fact]
    public void EmptyState_Is_Friendly()
    {
        Assert.Equal("All quiet… for now 🤫", BlotterFormatter.EmptyState);
    }
}
