using System;
using System.Collections.Generic;
using System.Linq;
using NoiseSnitch.Model;
using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Aggregation math, sorting, percentage rounding, and empty-window behaviour
/// behind the issue #23 daily digest. All pure — no WinForms, no live audio.
/// </summary>
public sealed class DigestTests
{
    private static readonly DateTime Now = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    private static NoiseEvent Ev(string proc, uint pid = 100, DateTime? ts = null) =>
        new(ts ?? Now, pid, proc, 0.5f, string.Empty);

    private static IEnumerable<NoiseEvent> Many(string proc, int n, uint pid = 100) =>
        Enumerable.Range(0, n).Select(_ => Ev(proc, pid));

    [Fact]
    public void Build_Totals_All_Events()
    {
        var events = Many("slack", 3, pid: 1)
            .Concat(Many("chrome", 5, pid: 2))
            .ToList();

        var digest = DigestBuilder.Build(events);

        Assert.Equal(8, digest.Total);
    }

    [Fact]
    public void Build_Sorts_Breakdown_By_Count_Descending()
    {
        var events = Many("slack", 3, pid: 1)
            .Concat(Many("chrome", 5, pid: 2))
            .Concat(Many("zoom", 1, pid: 3))
            .ToList();

        var digest = DigestBuilder.Build(events);

        Assert.Equal(new[] { 5, 3, 1 }, digest.Breakdown.Select(r => r.Count).ToArray());
    }

    [Fact]
    public void Build_Computes_Percentage_Shares()
    {
        // 5 + 3 + 2 = 10 → 50%, 30%, 20%.
        var events = Many("chrome", 5, pid: 1)
            .Concat(Many("slack", 3, pid: 2))
            .Concat(Many("zoom", 2, pid: 3))
            .ToList();

        var digest = DigestBuilder.Build(events);

        Assert.Equal(new[] { 50, 30, 20 }, digest.Breakdown.Select(r => r.Percent).ToArray());
    }

    [Fact]
    public void Build_Rounds_Percentages_To_Nearest_Whole()
    {
        // 1 of 3 = 33.33% → 33; 2 of 3 = 66.66% → 67.
        var events = Many("a", 1, pid: 1)
            .Concat(Many("b", 2, pid: 2))
            .ToList();

        var digest = DigestBuilder.Build(events);

        var pcts = digest.Breakdown.ToDictionary(r => r.ProcessName, r => r.Percent);
        Assert.Equal(67, pcts["b"]);
        Assert.Equal(33, pcts["a"]);
    }

    [Fact]
    public void Empty_Window_Yields_Zero_Total_And_No_Rows()
    {
        var digest = DigestBuilder.Build(Array.Empty<NoiseEvent>());

        Assert.Equal(0, digest.Total);
        Assert.Empty(digest.Breakdown);
    }

    [Fact]
    public void ForDay_Filters_To_Today_Local()
    {
        var yesterday = Now.AddDays(-1);
        var events = Many("today", 4, pid: 1)
            .Concat(Many("old", 9, pid: 2).Select(e => e with { TimestampUtc = yesterday }))
            .ToList();

        var digest = DigestBuilder.ForDay(events, Now);

        Assert.Equal(4, digest.Total);
        Assert.Single(digest.Breakdown);
        Assert.Equal("today", digest.Breakdown[0].ProcessName);
    }

    [Fact]
    public void Formatter_Renders_Empty_State()
    {
        var digest = DigestBuilder.Build(Array.Empty<NoiseEvent>());
        Assert.Equal(DigestFormatter.EmptyState, DigestFormatter.Render(digest));
    }

    [Fact]
    public void Formatter_Renders_Compact_Summary()
    {
        var events = Many("chrome", 5, pid: 1)
            .Concat(Many("slack", 5, pid: 2))
            .ToList();

        string text = DigestFormatter.Render(DigestBuilder.Build(events));

        Assert.StartsWith("10 sounds today —", text);
        Assert.Contains("50%", text);
    }

    [Fact]
    public void Formatter_Uses_Singular_Noun_For_One_Sound()
    {
        var digest = DigestBuilder.Build(Many("solo", 1, pid: 1).ToList());
        Assert.StartsWith("1 sound today —", DigestFormatter.Render(digest));
    }

    [Fact]
    public void Formatter_Truncates_Long_Breakdowns_With_Ellipsis()
    {
        var events = new List<NoiseEvent>();
        for (uint i = 1; i <= 7; i++)
        {
            events.AddRange(Many($"app{i}", (int)(8 - i), pid: i));
        }

        string text = DigestFormatter.Render(DigestBuilder.Build(events));

        Assert.Contains("…", text);
    }
}
