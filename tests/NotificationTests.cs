using System;
using System.Collections.Generic;
using NoiseSnitch.AudioWatcher;
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
        Assert.Equal(PersonalityCatalog.Default.PhraseEvent("System sounds") + " (now)", body);
    }

    [Fact]
    public void Ignored_Onsets_Do_Not_Reach_Notification_Stream()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var watcher = new SessionWatcher(
            ignore: new IgnoreList(new[] { "Spotify.exe" }),
            snapshot: () => new[]
            {
                new AudioSessionSnapshot(now, 10, "spotify", "", 0.5f, true),
                new AudioSessionSnapshot(now, 20, "chrome", "", 0.5f, true),
            });
        var notifications = new List<string>();
        watcher.Events.Added += (_, e) =>
            notifications.Add(NotificationFormatter.Body(e, PersonalityCatalog.Default, now));

        watcher.Poll();

        Assert.Equal(
            PersonalityCatalog.Default.PhraseEvent("Google Chrome") + " (now)",
            Assert.Single(notifications));
        Assert.Equal("chrome", Assert.Single(watcher.Events.Recent()).ProcessName);
    }

    [Fact]
    public void Debounce_Suppresses_Notifications_Until_Release()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        float peak = 0.5f;
        using var watcher = new SessionWatcher(
            detectorOptions: new EdgeDetectorOptions(0.1f, TimeSpan.FromSeconds(1)),
            snapshot: () => new[]
            {
                new AudioSessionSnapshot(now, 20, "chrome", "", peak, true),
            });
        var notifications = new List<string>();
        watcher.Events.Added += (_, e) =>
            notifications.Add(NotificationFormatter.Body(e, PersonalityCatalog.Default, now));

        watcher.Poll();
        now = now.AddMilliseconds(500);
        watcher.Poll(); // Continuous sound.
        peak = 0;
        watcher.Poll();
        now = now.AddMilliseconds(500);
        peak = 0.5f;
        watcher.Poll(); // A brief dip does not re-arm.
        Assert.Single(notifications);

        peak = 0;
        watcher.Poll();
        now = now.AddSeconds(1);
        peak = 0.5f;
        watcher.Poll();
        Assert.Equal(2, notifications.Count);
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
