using System;
using NoiseSnitch.Model;
using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Wording of the pure <see cref="QuietHoursAlertFormatter"/> \u2014 the tray-balloon
/// text shown when an app makes a sound during quiet hours (issue #8). It must
/// share the blotter's <see cref="FriendlyName"/> resolution so the toast names
/// the culprit exactly the way the row does.
/// </summary>
public sealed class QuietHoursAlertFormatterTests
{
    private static NoiseEvent Event(uint pid, string processName) =>
        new(DateTime.UtcNow, pid, processName, 0.5f, SessionName: "");

    [Fact]
    public void Title_Is_The_Fixed_Alert_Banner()
    {
        Assert.Equal("🔊 Noise during quiet hours", QuietHoursAlertFormatter.AlertTitle);
    }

    [Fact]
    public void Body_Uses_Friendly_Name_For_Known_App()
    {
        // "chrome" -> "Google Chrome" via the shared FriendlyName table.
        Assert.Equal(
            "Google Chrome just made a sound during your quiet hours.",
            QuietHoursAlertFormatter.Body(4821, "chrome"));
    }

    [Fact]
    public void Body_Prettifies_Unknown_App()
    {
        Assert.Equal(
            "My Cool App just made a sound during your quiet hours.",
            QuietHoursAlertFormatter.Body(123, "my_cool_app"));
    }

    [Fact]
    public void Body_Handles_System_Sounds_Pid_Zero()
    {
        Assert.Equal(
            "System sounds just made a sound during your quiet hours.",
            QuietHoursAlertFormatter.Body(0, ""));
    }

    [Fact]
    public void Body_Falls_Back_To_Pid_When_Name_Blank()
    {
        Assert.Equal(
            "pid 777 just made a sound during your quiet hours.",
            QuietHoursAlertFormatter.Body(777, ""));
    }

    [Fact]
    public void Event_Overload_Matches_The_Explicit_One()
    {
        var e = Event(4821, "chrome.exe");
        Assert.Equal(
            QuietHoursAlertFormatter.Body(e.ProcessId, e.ProcessName),
            QuietHoursAlertFormatter.Body(e));
    }
}
