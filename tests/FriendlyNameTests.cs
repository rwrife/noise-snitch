using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Pure raw-name → friendly-name mapping behind the M5 blotter labels. No
/// WinForms and no live process lookup is touched.
/// </summary>
public sealed class FriendlyNameTests
{
    [Theory]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("chrome.exe", "Google Chrome")]
    [InlineData("CHROME.EXE", "Google Chrome")] // case-insensitive key + suffix
    [InlineData("msedge", "Microsoft Edge")]
    [InlineData("discord", "Discord")]
    [InlineData("code", "Visual Studio Code")]
    public void From_Maps_Known_Apps(string raw, string expected)
    {
        Assert.Equal(expected, FriendlyName.From(raw));
    }

    [Theory]
    [InlineData("my_cool_app", "My Cool App")]
    [InlineData("some-random-thing", "Some Random Thing")]
    [InlineData("weatherwidget", "Weatherwidget")]
    public void From_Prettifies_Unknown_Names(string raw, string expected)
    {
        Assert.Equal(expected, FriendlyName.From(raw));
    }

    [Theory]
    [InlineData("iTunes")]   // intentional mixed case preserved
    [InlineData("MyApp")]    // unknown mixed-case token left intact
    public void From_Preserves_Intentional_Casing_For_Unknowns(string raw)
    {
        // The prettifier must not force-lower an already-cased token. These names
        // are not in the known map, so they flow through Prettify unchanged.
        string result = FriendlyName.From(raw);
        Assert.Contains(raw.Split('.')[0], result, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    public void From_Blank_Returns_Unknown(string? raw)
    {
        Assert.Equal(FriendlyName.Unknown, FriendlyName.From(raw));
    }

    [Fact]
    public void ForEvent_Pid0_Is_System_Sounds()
    {
        Assert.Equal(FriendlyName.SystemSounds, FriendlyName.ForEvent(0, ""));
        Assert.Equal(FriendlyName.SystemSounds, FriendlyName.ForEvent(0, "anything"));
    }

    [Fact]
    public void ForEvent_Maps_Known_App()
    {
        Assert.Equal("Spotify", FriendlyName.ForEvent(4821, "spotify.exe"));
    }

    [Fact]
    public void ForEvent_Falls_Back_To_Pid_When_Name_Blank()
    {
        Assert.Equal("pid 4821", FriendlyName.ForEvent(4821, "   "));
    }
}
