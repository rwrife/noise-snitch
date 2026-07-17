using System;
using NoiseSnitch.Model;
using NoiseSnitch.Personality;
using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Issue #29 "Notification-only mode": the pure phrasing for a per-event toast
/// (<see cref="NotificationFormatter"/>) and the mode enum's persistence /
/// normalization on <see cref="Settings"/>. UI/WASAPI are untouched — the tray
/// only wires these decisions to <c>NotifyIcon.ShowBalloonTip</c>.
/// </summary>
public sealed class NotificationTests
{
    private static NoiseEvent Event(
        uint pid = 4821,
        string name = "chrome",
        DateTime? whenUtc = null) =>
        new(whenUtc ?? DateTime.UtcNow, pid, name, 0.42f, "Chrome");

    [Fact]
    public void Body_Runs_Through_Active_Personality_Pack()
    {
        var e = Event(name: "chrome");
        var now = e.TimestampUtc; // "now"

        var deadpan = PersonalityCatalog.Resolve("deadpan");
        var gremlin = PersonalityCatalog.Resolve("gremlin");

        string dead = NotificationFormatter.Body(e, deadpan, now);
        string grem = NotificationFormatter.Body(e, gremlin, now);

        // Same event, different pack -> different wording. Both name the culprit
        // via the shared FriendlyName resolution ("chrome" -> "Google Chrome").
        Assert.Contains("Google Chrome", dead);
        Assert.Contains("Google Chrome", grem);
        Assert.Equal(deadpan.PhraseEvent("Google Chrome") + " (now)", dead);
        Assert.NotEqual(dead, grem);
    }

    [Fact]
    public void Body_Tags_Relative_Time()
    {
        var e = Event(whenUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var now = e.TimestampUtc.AddSeconds(5);

        string body = NotificationFormatter.Body(e, PersonalityCatalog.Default, now);

        Assert.EndsWith("(5s ago)", body);
    }

    [Fact]
    public void Body_Names_System_Sounds_For_Pid_Zero()
    {
        var e = Event(pid: 0, name: "");
        string body = NotificationFormatter.Body(e, PersonalityCatalog.Default, e.TimestampUtc);
        Assert.Contains("System Sounds", body);
    }

    [Fact]
    public void Default_Mode_Is_Flash()
    {
        Assert.Equal(NotificationMode.Flash, Settings.Defaults().NotificationMode);
        Assert.Equal(NotificationMode.Flash, Settings.DefaultNotificationMode);
    }

    [Fact]
    public void Normalized_Preserves_Valid_Modes()
    {
        foreach (var mode in new[]
        {
            NotificationMode.Flash, NotificationMode.Toast, NotificationMode.Both,
        })
        {
            var s = new Settings { NotificationMode = mode }.Normalized();
            Assert.Equal(mode, s.NotificationMode);
        }
    }

    [Fact]
    public void Normalized_Snaps_Unknown_Mode_Back_To_Default()
    {
        // A hand-edited file could carry a bogus numeric value; it must not reach
        // the runtime as an undefined enum.
        var s = new Settings { NotificationMode = (NotificationMode)99 }.Normalized();
        Assert.Equal(Settings.DefaultNotificationMode, s.NotificationMode);
    }
}
