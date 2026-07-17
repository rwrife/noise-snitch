using System;
using System.IO;
using NoiseSnitch.Config;
using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Round-trip + robustness of <see cref="SettingsStore"/> (M5 persistence).
/// Uses a throwaway temp file per test so nothing touches the real
/// <c>%LOCALAPPDATA%</c> location.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _path;

    public SettingsStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"noise-snitch-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        TryDelete(_path);
        TryDelete(_path + ".tmp");
    }

    private static void TryDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
    }

    [Fact]
    public void Load_Missing_File_Returns_Defaults()
    {
        var store = new SettingsStore(_path);
        var s = store.Load();
        Assert.Equal(Settings.DefaultPollIntervalMs, s.PollIntervalMs);
        Assert.Equal(Settings.DefaultEventsToKeep, s.EventsToKeep);
    }

    [Fact]
    public void Save_Then_Load_Round_Trips_Values()
    {
        var store = new SettingsStore(_path);
        var original = new Settings
        {
            PollIntervalMs = 400,
            EventsToKeep = 321,
            PeakThreshold = 0.2f,
            ReleaseMs = 750,
            QuietHoursEnabled = true,
            QuietHoursStart = "23:00",
            QuietHoursEnd = "06:30",
            NotificationMode = NotificationMode.Both,
        };

        Assert.True(store.Save(original));
        Assert.True(File.Exists(_path));

        var loaded = store.Load();
        Assert.Equal(400, loaded.PollIntervalMs);
        Assert.Equal(321, loaded.EventsToKeep);
        Assert.Equal(0.2f, loaded.PeakThreshold);
        Assert.Equal(750, loaded.ReleaseMs);
        Assert.True(loaded.QuietHoursEnabled);
        Assert.Equal("23:00", loaded.QuietHoursStart);
        Assert.Equal("06:30", loaded.QuietHoursEnd);
    }

    [Fact]
    public void NotificationMode_Round_Trips_By_Name()
    {
        // Issue #29: the mode persists (as a readable name) and reloads intact.
        var store = new SettingsStore(_path);
        Assert.True(store.Save(new Settings { NotificationMode = NotificationMode.Both }));
        Assert.Contains("\"Both\"", File.ReadAllText(_path));
        Assert.Equal(NotificationMode.Both, store.Load().NotificationMode);
    }

    [Fact]
    public void Load_HandEdited_NotificationMode_Name_Is_Case_Insensitive()
    {
        // Issue #29: a user typing "toast" (lower-case) still resolves.
        File.WriteAllText(_path, "{ \"NotificationMode\": \"toast\" }");
        Assert.Equal(NotificationMode.Toast, new SettingsStore(_path).Load().NotificationMode);
    }

    [Fact]
    public void Load_HandEdited_Unknown_NotificationMode_Falls_Back_To_Default()
    {
        // A bogus numeric value must not reach the runtime as an undefined enum.
        File.WriteAllText(_path, "{ \"NotificationMode\": 99 }");
        Assert.Equal(Settings.DefaultNotificationMode,
            new SettingsStore(_path).Load().NotificationMode);
    }

    [Fact]
    public void Load_HandEdited_QuietHours_Parses_And_Canonicalizes()
    {
        // Issue #8: a user types a sloppy window into settings.json; it should load
        // enabled with the window canonicalized to HH:mm.
        File.WriteAllText(_path,
            "{ \"QuietHoursEnabled\": true, \"QuietHoursStart\": \"9:5\", \"QuietHoursEnd\": \"17:0\" }");
        var loaded = new SettingsStore(_path).Load();

        Assert.True(loaded.QuietHoursEnabled);
        Assert.Equal("09:05", loaded.QuietHoursStart); // Load normalizes
        Assert.Equal("17:00", loaded.QuietHoursEnd);
        Assert.Equal(9 * 60 + 5, loaded.QuietHoursStartMinute);
        Assert.Equal(17 * 60, loaded.QuietHoursEndMinute);
    }

    [Fact]
    public void Load_Normalizes_Out_Of_Range_File_Values()
    {
        // Simulate a hand-edit that puts a zero poll interval on disk.
        File.WriteAllText(_path, "{ \"PollIntervalMs\": 0, \"EventsToKeep\": 999999 }");
        var loaded = new SettingsStore(_path).Load();

        Assert.Equal(Settings.DefaultPollIntervalMs, loaded.PollIntervalMs); // 0 -> default
        Assert.Equal(Settings.MaxEventsToKeep, loaded.EventsToKeep);         // clamped down
    }

    [Fact]
    public void Load_Corrupt_Json_Returns_Defaults()
    {
        File.WriteAllText(_path, "{ this is not valid json ");
        var loaded = new SettingsStore(_path).Load();
        Assert.Equal(Settings.DefaultPollIntervalMs, loaded.PollIntervalMs);
    }

    [Fact]
    public void Load_Empty_File_Returns_Defaults()
    {
        File.WriteAllText(_path, "   ");
        var loaded = new SettingsStore(_path).Load();
        Assert.Equal(Settings.DefaultEventsToKeep, loaded.EventsToKeep);
    }

    [Fact]
    public void Load_Tolerates_Comments_And_Trailing_Commas()
    {
        File.WriteAllText(_path,
            "{\n  // hand-edited\n  \"PollIntervalMs\": 500,\n  \"EventsToKeep\": 50,\n}");
        var loaded = new SettingsStore(_path).Load();
        Assert.Equal(500, loaded.PollIntervalMs);
        Assert.Equal(50, loaded.EventsToKeep);
    }

    [Fact]
    public void Load_Partial_File_Fills_Missing_With_Defaults()
    {
        File.WriteAllText(_path, "{ \"EventsToKeep\": 42 }");
        var loaded = new SettingsStore(_path).Load();
        Assert.Equal(42, loaded.EventsToKeep);
        Assert.Equal(Settings.DefaultPollIntervalMs, loaded.PollIntervalMs); // absent -> default
    }

    [Fact]
    public void Save_Overwrites_Existing_File()
    {
        var store = new SettingsStore(_path);
        Assert.True(store.Save(new Settings { EventsToKeep = 10 }));
        Assert.True(store.Save(new Settings { EventsToKeep = 20 }));
        Assert.Equal(20, store.Load().EventsToKeep);
    }
}
