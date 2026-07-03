using System;
using System.Collections.Generic;
using NoiseSnitch.Model;
using NoiseSnitch.Persistence;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Pure formatting of the M6 "copy last hour" report (<see cref="NoiseExport"/>).
/// No clipboard / UI involved — just the text a user pastes.
/// </summary>
public sealed class NoiseExportTests
{
    private static NoiseEvent Event(DateTime tUtc, uint pid, string name, float peak = 0.5f) =>
        new(tUtc, pid, name, peak, string.Empty);

    private static readonly DateTime Now = new(2026, 7, 2, 14, 5, 12, DateTimeKind.Utc);

    [Fact]
    public void Empty_Set_Is_A_Friendly_One_Liner()
    {
        string report = NoiseExport.Report(Array.Empty<NoiseEvent>(), NoiseExport.LastHourWindow, Now);
        Assert.Contains("no events", report);
        Assert.Contains(NoiseExport.LastHourWindow, report);
        // No table / tally when there's nothing.
        Assert.DoesNotContain("Top offenders", report);
    }

    [Fact]
    public void Header_Counts_And_Labels_The_Window()
    {
        var events = new List<NoiseEvent>
        {
            Event(Now.AddMinutes(-1), 1, "chrome"),
            Event(Now.AddMinutes(-2), 2, "slack"),
        };

        string report = NoiseExport.Report(events, NoiseExport.LastHourWindow, Now);
        Assert.Contains("2 events", report);
        Assert.Contains("(last hour)", report);
    }

    [Fact]
    public void Singular_Event_Uses_Singular_Noun()
    {
        var events = new List<NoiseEvent> { Event(Now.AddMinutes(-1), 1, "chrome") };
        string report = NoiseExport.Report(events, NoiseExport.LastHourWindow, Now);
        Assert.Contains("1 event (", report);
        Assert.DoesNotContain("1 events", report);
    }

    [Fact]
    public void Rows_Use_Friendly_Names_And_System_Sounds()
    {
        var events = new List<NoiseEvent>
        {
            Event(Now.AddMinutes(-1), 100, "chrome"),   // -> Google Chrome
            Event(Now.AddMinutes(-2), 0, "irrelevant"), // pid 0 -> System sounds
        };

        string report = NoiseExport.Report(events, NoiseExport.LastHourWindow, Now);
        Assert.Contains("Google Chrome", report);
        Assert.Contains("System sounds", report);
    }

    [Fact]
    public void Tally_Orders_By_Frequency_Descending()
    {
        var events = new List<NoiseEvent>
        {
            Event(Now.AddMinutes(-1), 1, "slack"),
            Event(Now.AddMinutes(-2), 2, "chrome"),
            Event(Now.AddMinutes(-3), 3, "chrome"),
            Event(Now.AddMinutes(-4), 4, "chrome"),
        };

        string report = NoiseExport.Report(events, NoiseExport.LastHourWindow, Now);
        Assert.Contains("Top offenders:", report);
        Assert.Contains("Google Chrome ×3", report);
        Assert.Contains("Slack ×1", report);

        // Chrome (×3) must appear before Slack (×1) in the tally line.
        int idxTally = report.IndexOf("Top offenders:", StringComparison.Ordinal);
        string tail = report[idxTally..];
        Assert.True(
            tail.IndexOf("Google Chrome", StringComparison.Ordinal)
            < tail.IndexOf("Slack", StringComparison.Ordinal),
            "more frequent app should be listed first");
    }

    [Fact]
    public void Report_Includes_Peak_Values()
    {
        var events = new List<NoiseEvent> { Event(Now.AddMinutes(-1), 1, "chrome", peak: 0.42f) };
        string report = NoiseExport.Report(events, NoiseExport.LastHourWindow, Now);
        Assert.Contains("peak 0.42", report);
    }
}
