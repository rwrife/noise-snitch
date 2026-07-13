using System;
using System.Collections.Generic;
using System.Linq;
using NoiseSnitch.Model;
using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Aggregation, ranking, and rendering rules behind the issue #22 noise
/// leaderboard. All pure — no WinForms, no live audio.
/// </summary>
public sealed class LeaderboardTests
{
    private static readonly DateTime Now = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    private static NoiseEvent Ev(string proc, uint pid = 100, DateTime? ts = null) =>
        new(ts ?? Now, pid, proc, 0.5f, string.Empty);

    private static IReadOnlyList<NoiseEvent> Many(string proc, int n, uint pid = 100)
    {
        var list = new List<NoiseEvent>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(Ev(proc, pid));
        }

        return list;
    }

    [Fact]
    public void Rank_Counts_Events_Per_App_Descending()
    {
        var events = Many("slack", 3, pid: 1)
            .Concat(Many("chrome", 5, pid: 2))
            .Concat(Many("zoom", 1, pid: 3))
            .ToList();

        var rows = Leaderboard.Rank(events);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Google Chrome", "Slack", "Zoom" },
            rows.Select(r => FriendlyName.ForEvent(r.ProcessId, r.ProcessName)).ToArray());
        Assert.Equal(new[] { 5, 3, 1 }, rows.Select(r => r.Count).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Rank).ToArray());
    }

    [Fact]
    public void Rank_Breaks_Ties_By_Friendly_Name_Ascending()
    {
        // slack and zoom both have 2 → alphabetical by display name: Slack, Zoom.
        var events = Many("zoom", 2, pid: 1).Concat(Many("slack", 2, pid: 2)).ToList();

        var rows = Leaderboard.Rank(events);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Slack", FriendlyName.ForEvent(rows[0].ProcessId, rows[0].ProcessName));
        Assert.Equal("Zoom", FriendlyName.ForEvent(rows[1].ProcessId, rows[1].ProcessName));
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal(2, rows[1].Rank);
    }

    [Fact]
    public void Rank_Merges_Exe_Suffix_And_Casing_Into_One_Bucket()
    {
        // "chrome", "chrome.exe", "Chrome" are the same app: one row, count 3.
        var events = new[]
        {
            Ev("chrome", pid: 1),
            Ev("chrome.exe", pid: 2),
            Ev("Chrome", pid: 3),
        };

        var rows = Leaderboard.Rank(events);

        Assert.Single(rows);
        Assert.Equal(3, rows[0].Count);
    }

    [Fact]
    public void Rank_Keeps_System_Sounds_As_Its_Own_Bucket()
    {
        // pid 0 is the system-sounds session regardless of name.
        var events = new[]
        {
            Ev("", pid: 0),
            Ev("", pid: 0),
            Ev("chrome", pid: 1),
        };

        var rows = Leaderboard.Rank(events);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        Assert.Equal(FriendlyName.SystemSounds,
            FriendlyName.ForEvent(rows[0].ProcessId, rows[0].ProcessName));
    }

    [Fact]
    public void Rank_Empty_Input_Yields_No_Rows()
    {
        Assert.Empty(Leaderboard.Rank(Array.Empty<NoiseEvent>()));
    }

    [Fact]
    public void Rank_Single_App_Is_One_Row_At_Rank_One()
    {
        var rows = Leaderboard.Rank(Many("firefox", 7));
        Assert.Single(rows);
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal(7, rows[0].Count);
    }

    [Fact]
    public void ForDay_Filters_To_Todays_Local_Events()
    {
        DateTime today = Now;                 // 2026-07-12 (local of UTC noon)
        DateTime yesterday = Now.AddDays(-1);
        var events = new[]
        {
            Ev("chrome", pid: 1, ts: today),
            Ev("chrome", pid: 1, ts: today),
            Ev("slack", pid: 2, ts: yesterday),  // dropped: not today
        };

        var rows = Leaderboard.ForDay(events, Now);

        Assert.Single(rows);
        Assert.Equal("Google Chrome",
            FriendlyName.ForEvent(rows[0].ProcessId, rows[0].ProcessName));
        Assert.Equal(2, rows[0].Count);
    }

    [Fact]
    public void Formatter_Line_Shows_Medals_For_Top_Three()
    {
        var rows = new[]
        {
            new LeaderboardRow(1, 1, "slack", 38),
            new LeaderboardRow(2, 2, "chrome", 22),
            new LeaderboardRow(3, 3, "zoom", 9),
            new LeaderboardRow(4, 4, "firefox", 4),
        };

        Assert.Equal("1. 🥇 Slack — 38", LeaderboardFormatter.Line(rows[0]));
        Assert.Equal("2. 🥈 Google Chrome — 22", LeaderboardFormatter.Line(rows[1]));
        Assert.Equal("3. 🥉 Zoom — 9", LeaderboardFormatter.Line(rows[2]));
        Assert.Equal("4. Firefox — 4", LeaderboardFormatter.Line(rows[3]));
    }

    [Fact]
    public void Formatter_Line_Can_Suppress_Medals()
    {
        var row = new LeaderboardRow(1, 1, "slack", 38);
        Assert.Equal("1. Slack — 38", LeaderboardFormatter.Line(row, withMedals: false));
    }

    [Fact]
    public void Formatter_Render_Joins_Lines()
    {
        var rows = new[]
        {
            new LeaderboardRow(1, 1, "slack", 3),
            new LeaderboardRow(2, 2, "chrome", 1),
        };

        Assert.Equal("1. 🥇 Slack — 3\n2. 🥈 Google Chrome — 1",
            LeaderboardFormatter.Render(rows));
    }

    [Fact]
    public void Formatter_Render_Empty_Shows_Empty_State()
    {
        Assert.Equal(LeaderboardFormatter.EmptyState,
            LeaderboardFormatter.Render(Array.Empty<LeaderboardRow>()));
    }
}
