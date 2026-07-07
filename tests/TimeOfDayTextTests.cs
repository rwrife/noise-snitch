using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Parsing/formatting rules for the pure <see cref="TimeOfDayText"/> helper \u2014 the
/// one place that turns a hand-editable <c>"HH:mm"</c> string (the quiet-hours
/// window, issue #8) into minutes past midnight and back. Being forgiving of
/// hand-edits while rejecting genuine nonsense is the whole job.
/// </summary>
public sealed class TimeOfDayTextTests
{
    private const int Fallback = 22 * 60; // 22:00, an arbitrary sentinel for these tests

    private static int Hm(int hour, int minute) => (hour * 60) + minute;

    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("07:00", 7 * 60)]
    [InlineData("22:30", 22 * 60 + 30)]
    [InlineData("23:59", 23 * 60 + 59)]
    public void Parses_Canonical_HHmm(string text, int expected)
    {
        Assert.Equal(expected, TimeOfDayText.ParseToMinuteOfDay(text, Fallback));
    }

    [Theory]
    [InlineData("9:05", 9 * 60 + 5)]   // single-digit hour
    [InlineData("  8:00  ", 8 * 60)]   // surrounding whitespace
    [InlineData("7:5", 7 * 60 + 5)]    // single-digit minute
    public void Parses_Forgivingly(string text, int expected)
    {
        Assert.Equal(expected, TimeOfDayText.ParseToMinuteOfDay(text, Fallback));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("1234")]      // no colon
    [InlineData("12:")]       // trailing colon, empty minute
    [InlineData(":30")]       // leading colon, empty hour
    [InlineData("24:00")]     // hour out of range (typo, not intent)
    [InlineData("22:60")]     // minute out of range
    [InlineData("-1:00")]     // negative hour
    [InlineData("9:5:1")]     // seconds not supported -> minute part "5:1" fails
    [InlineData("12:ab")]     // non-numeric minute
    public void Falls_Back_On_Junk(string? text)
    {
        Assert.Equal(Fallback, TimeOfDayText.ParseToMinuteOfDay(text, Fallback));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(7 * 60, "07:00")]
    [InlineData(22 * 60 + 5, "22:05")]
    [InlineData(23 * 60 + 59, "23:59")]
    public void Formats_ZeroPadded(int minuteOfDay, string expected)
    {
        Assert.Equal(expected, TimeOfDayText.FromMinuteOfDay(minuteOfDay));
    }

    [Fact]
    public void Format_Wraps_OutOfRange_Rather_Than_Throwing()
    {
        Assert.Equal("01:00", TimeOfDayText.FromMinuteOfDay(25 * 60)); // 1500 -> 01:00
        Assert.Equal("23:00", TimeOfDayText.FromMinuteOfDay(-60));     // -60 -> 23:00
    }

    [Fact]
    public void Parse_Then_Format_Is_Canonicalizing()
    {
        // A sloppy "9:5" round-trips to the canonical "09:05".
        int m = TimeOfDayText.ParseToMinuteOfDay("9:5", Fallback);
        Assert.Equal(Hm(9, 5), m);
        Assert.Equal("09:05", TimeOfDayText.FromMinuteOfDay(m));
    }
}
