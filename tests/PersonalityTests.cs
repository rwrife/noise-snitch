using System;
using System.Linq;
using NoiseSnitch.Model;
using NoiseSnitch.Personality;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Issue #24: personality packs. Guards that every built-in pack supplies
/// non-empty strings for each slot, that a picked pack round-trips through
/// <see cref="Settings"/> persistence normalization, and that an unknown/missing
/// key safely falls back to the default voice.
/// </summary>
public sealed class PersonalityTests
{
    [Fact]
    public void Catalog_Is_NonEmpty_And_Default_Is_Present()
    {
        Assert.NotEmpty(PersonalityCatalog.All);
        Assert.Contains(PersonalityCatalog.All, p => p.Key == PersonalityCatalog.DefaultKey);
        Assert.Equal(PersonalityCatalog.DefaultKey, PersonalityCatalog.Default.Key);
    }

    [Fact]
    public void Every_Pack_Fills_Every_Slot_With_NonEmpty_Text()
    {
        foreach (SnitchPersonality pack in PersonalityCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(pack.Key), $"{pack.DisplayName}: key");
            Assert.False(string.IsNullOrWhiteSpace(pack.DisplayName), $"{pack.Key}: displayName");
            Assert.False(string.IsNullOrWhiteSpace(pack.TrayTooltip), $"{pack.Key}: tooltip");
            Assert.False(string.IsNullOrWhiteSpace(pack.BlotterEmptyState), $"{pack.Key}: emptyState");

            string phrased = pack.PhraseEvent("chrome.exe");
            Assert.False(string.IsNullOrWhiteSpace(phrased), $"{pack.Key}: event phrasing");
            // The culprit must actually appear in the phrasing.
            Assert.Contains("chrome.exe", phrased);
        }
    }

    [Fact]
    public void Keys_Are_Unique()
    {
        var keys = PersonalityCatalog.All.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("gremlin")]
    [InlineData("GREMLIN")]
    [InlineData("  Gremlin  ")]
    public void Resolve_Is_Case_And_Whitespace_Insensitive(string key)
    {
        Assert.Equal("gremlin", PersonalityCatalog.Resolve(key).Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("does-not-exist")]
    public void Resolve_Falls_Back_To_Default_For_Unknown_Or_Missing(string? key)
    {
        Assert.Equal(PersonalityCatalog.DefaultKey, PersonalityCatalog.Resolve(key).Key);
        Assert.False(PersonalityCatalog.IsKnown(key));
    }

    [Fact]
    public void Settings_Default_Pack_Is_The_Catalog_Default()
    {
        Assert.Equal(PersonalityCatalog.DefaultKey, Settings.Defaults().PersonalityPack);
    }

    [Fact]
    public void Settings_Normalize_Canonicalizes_A_Valid_Pack_Key()
    {
        var s = new Settings { PersonalityPack = "  DEADPAN  " }.Normalized();
        Assert.Equal("deadpan", s.PersonalityPack);
    }

    [Fact]
    public void Settings_Normalize_Snaps_Unknown_Pack_To_Default()
    {
        var s = new Settings { PersonalityPack = "nonsense" }.Normalized();
        Assert.Equal(PersonalityCatalog.DefaultKey, s.PersonalityPack);
    }

    [Fact]
    public void PhraseEvent_Tolerates_Null_Culprit()
    {
        // Should not throw; a null name is treated as empty.
        string phrased = PersonalityCatalog.Default.PhraseEvent(null!);
        Assert.NotNull(phrased);
    }
}
