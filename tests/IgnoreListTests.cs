using System;
using System.Linq;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Matching and filtering rules of the pure <see cref="IgnoreList"/> — the brain
/// behind the v0.2 "Per-app rules / allowlist" feature (issue #9). Rules are
/// matched case-insensitively with a trailing <c>.exe</c> stripped, so a user who
/// types <c>Spotify</c>, <c>spotify</c>, or <c>spotify.exe</c> silences the same
/// app. Everything here is deterministic and clock-free.
/// </summary>
public sealed class IgnoreListTests
{
    private static NoiseEvent Event(string processName) =>
        new(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc), 42, processName, 0.3f, "");

    [Fact]
    public void Empty_List_Ignores_Nothing()
    {
        Assert.Equal(0, IgnoreList.Empty.Count);
        Assert.False(IgnoreList.Empty.IsIgnored("chrome"));
    }

    [Fact]
    public void Null_Rules_Is_Empty()
    {
        var list = new IgnoreList(null);
        Assert.Equal(0, list.Count);
        Assert.False(list.IsIgnored("anything"));
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("Spotify")]
    [InlineData("SPOTIFY")]
    [InlineData("spotify.exe")]
    [InlineData("Spotify.EXE")]
    [InlineData("  spotify.exe  ")]
    public void Matches_Case_Insensitively_And_Strips_Exe(string query)
    {
        var list = new IgnoreList(new[] { "Spotify.exe" });
        Assert.True(list.IsIgnored(query));
    }

    [Fact]
    public void Non_Matching_App_Is_Not_Ignored()
    {
        var list = new IgnoreList(new[] { "spotify" });
        Assert.False(list.IsIgnored("chrome"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_Rules_Are_Dropped(string? rule)
    {
        var list = new IgnoreList(new[] { rule!, "chrome" });
        Assert.Equal(1, list.Count);
        Assert.True(list.IsIgnored("chrome"));
        Assert.False(list.IsIgnored(""));
    }

    [Fact]
    public void Duplicate_And_Variant_Rules_Collapse_To_One()
    {
        var list = new IgnoreList(new[] { "Spotify", "spotify.exe", "SPOTIFY" });
        Assert.Equal(1, list.Count);
        Assert.Single(list.Rules);
        Assert.Equal("spotify", list.Rules[0]);
    }

    [Fact]
    public void IsIgnored_Overload_Reads_Event_ProcessName()
    {
        var list = new IgnoreList(new[] { "chrome" });
        Assert.True(list.IsIgnored(Event("chrome.exe")));
        Assert.False(list.IsIgnored(Event("firefox")));
    }

    [Fact]
    public void Filter_Drops_Ignored_Events_Preserving_Order()
    {
        var list = new IgnoreList(new[] { "spotify" });
        var events = new[]
        {
            Event("chrome"),
            Event("spotify"),
            Event("discord"),
            Event("Spotify.exe"),
            Event("firefox"),
        };

        var kept = list.Filter(events).Select(e => e.ProcessName).ToArray();
        Assert.Equal(new[] { "chrome", "discord", "firefox" }, kept);
    }

    [Fact]
    public void Filter_Of_Null_Is_Empty()
    {
        Assert.Empty(new IgnoreList(new[] { "x" }).Filter(null!));
    }

    [Fact]
    public void With_Adds_An_App_Without_Mutating_Original()
    {
        var original = IgnoreList.Empty;
        var updated = original.With("spotify.exe");

        Assert.Equal(0, original.Count);
        Assert.True(updated.IsIgnored("spotify"));
        Assert.Equal(1, updated.Count);
    }

    [Fact]
    public void With_Existing_Or_Blank_Returns_Equivalent_List()
    {
        var list = new IgnoreList(new[] { "spotify" });
        Assert.Same(list, list.With("Spotify.exe")); // already present
        Assert.Same(list, list.With("   "));         // blank no-op
    }

    [Fact]
    public void Without_Removes_An_App_Without_Mutating_Original()
    {
        var original = new IgnoreList(new[] { "spotify", "chrome" });
        var updated = original.Without("Spotify.exe");

        Assert.True(original.IsIgnored("spotify"));
        Assert.False(updated.IsIgnored("spotify"));
        Assert.True(updated.IsIgnored("chrome"));
    }

    [Fact]
    public void Without_Missing_Or_Blank_Returns_Equivalent_List()
    {
        var list = new IgnoreList(new[] { "spotify" });
        Assert.Same(list, list.Without("chrome")); // not present
        Assert.Same(list, list.Without(""));       // blank no-op
    }

    [Fact]
    public void Rules_Are_Sorted_For_Stable_Display()
    {
        var list = new IgnoreList(new[] { "zoom", "chrome", "spotify" });
        Assert.Equal(new[] { "chrome", "spotify", "zoom" }, list.Rules.ToArray());
    }
}
