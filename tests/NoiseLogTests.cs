using System;
using System.IO;
using System.Linq;
using NoiseSnitch.Model;
using NoiseSnitch.Persistence;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Round-trip, windowing, rotation, and robustness of the M6 JSONL noise log
/// (<see cref="NoiseLog"/>). Uses a throwaway temp file per test so nothing
/// touches the real <c>%LOCALAPPDATA%</c> location.
/// </summary>
public sealed class NoiseLogTests : IDisposable
{
    private readonly string _path;

    public NoiseLogTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"noise-snitch-log-{Guid.NewGuid():N}.jsonl");
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

    private static NoiseEvent Event(DateTime tUtc, uint pid = 42, string name = "chrome", float peak = 0.5f, string session = "Chrome") =>
        new(tUtc, pid, name, peak, session);

    [Fact]
    public void ReadAll_Missing_File_Is_Empty()
    {
        var log = new NoiseLog(_path);
        Assert.Empty(log.ReadAll());
    }

    [Fact]
    public void Append_Then_ReadAll_Round_Trips_In_File_Order()
    {
        var log = new NoiseLog(_path);
        var t0 = new DateTime(2026, 7, 2, 14, 0, 0, DateTimeKind.Utc);

        Assert.True(log.Append(Event(t0, pid: 1, name: "chrome")));
        Assert.True(log.Append(Event(t0.AddSeconds(5), pid: 2, name: "slack")));

        var all = log.ReadAll();
        Assert.Equal(2, all.Count);
        // Oldest first (file order).
        Assert.Equal(1u, all[0].ProcessId);
        Assert.Equal("chrome", all[0].ProcessName);
        Assert.Equal(2u, all[1].ProcessId);
        Assert.Equal("slack", all[1].ProcessName);
    }

    [Fact]
    public void Round_Trip_Preserves_All_Fields_As_Utc()
    {
        var log = new NoiseLog(_path);
        var t = new DateTime(2026, 7, 2, 9, 30, 15, DateTimeKind.Utc);
        log.Append(Event(t, pid: 4821, name: "discord", peak: 0.123f, session: "Discord Voice"));

        var e = log.ReadAll().Single();
        Assert.Equal(t, e.TimestampUtc);
        Assert.Equal(DateTimeKind.Utc, e.TimestampUtc.Kind);
        Assert.Equal(4821u, e.ProcessId);
        Assert.Equal("discord", e.ProcessName);
        Assert.Equal(0.123f, e.Peak, 5);
        Assert.Equal("Discord Voice", e.SessionName);
    }

    [Fact]
    public void Local_Kind_Timestamp_Is_Normalized_To_Utc_On_Disk()
    {
        var log = new NoiseLog(_path);
        var local = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Local);
        log.Append(Event(local));

        var e = log.ReadAll().Single();
        Assert.Equal(local.ToUniversalTime(), e.TimestampUtc);
    }

    [Fact]
    public void ReadAll_Skips_Unparseable_Lines()
    {
        var log = new NoiseLog(_path);
        var t = new DateTime(2026, 7, 2, 14, 0, 0, DateTimeKind.Utc);
        log.Append(Event(t, name: "chrome"));

        // Simulate a torn write / hand-edit: inject a junk line and a blank line.
        File.AppendAllText(_path, "this is not json\n\n");
        log.Append(Event(t.AddSeconds(1), name: "slack"));

        var all = log.ReadAll();
        Assert.Equal(2, all.Count); // junk + blank skipped, two real events kept
        Assert.Equal("chrome", all[0].ProcessName);
        Assert.Equal("slack", all[1].ProcessName);
    }

    [Fact]
    public void ReadSince_Returns_Newest_First_Within_Window()
    {
        var log = new NoiseLog(_path);
        var now = new DateTime(2026, 7, 2, 15, 0, 0, DateTimeKind.Utc);

        log.Append(Event(now.AddHours(-3), name: "old"));      // outside 1h window
        log.Append(Event(now.AddMinutes(-40), name: "mid"));    // inside
        log.Append(Event(now.AddMinutes(-5), name: "recent"));  // inside

        var last = log.ReadSince(TimeSpan.FromHours(1), now);
        Assert.Equal(2, last.Count);
        Assert.Equal("recent", last[0].ProcessName); // newest first
        Assert.Equal("mid", last[1].ProcessName);
    }

    [Fact]
    public void ReadSince_Empty_When_Nothing_In_Window()
    {
        var log = new NoiseLog(_path);
        var now = new DateTime(2026, 7, 2, 15, 0, 0, DateTimeKind.Utc);
        log.Append(Event(now.AddDays(-1)));
        Assert.Empty(log.ReadSince(TimeSpan.FromHours(1), now));
    }

    [Fact]
    public void Append_Rotates_When_Over_Cap_Keeping_Newest()
    {
        // Tiny cap forces rotation after a handful of lines.
        var log = new NoiseLog(_path, maxBytes: 512);
        var t0 = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 200; i++)
        {
            log.Append(Event(t0.AddSeconds(i), pid: (uint)i, name: $"app{i}", session: "s"));
        }

        long size = new FileInfo(_path).Length;
        Assert.True(size <= 512, $"log should stay within cap, was {size}");

        var all = log.ReadAll();
        Assert.NotEmpty(all);
        // The newest event (app199) must survive; the oldest (app0) must be gone.
        Assert.Contains(all, e => e.ProcessName == "app199");
        Assert.DoesNotContain(all, e => e.ProcessName == "app0");
        // File order remains oldest -> newest among survivors.
        var pids = all.Select(e => (int)e.ProcessId).ToList();
        var sorted = pids.OrderBy(x => x).ToList();
        Assert.Equal(sorted, pids);
    }

    [Fact]
    public void Clear_Removes_The_File()
    {
        var log = new NoiseLog(_path);
        log.Append(Event(DateTime.UtcNow));
        Assert.True(File.Exists(_path));

        log.Clear();
        Assert.False(File.Exists(_path));
        Assert.Empty(log.ReadAll());
    }

    [Fact]
    public void Each_Append_Writes_Exactly_One_Line()
    {
        var log = new NoiseLog(_path);
        var t = new DateTime(2026, 7, 2, 14, 0, 0, DateTimeKind.Utc);
        log.Append(Event(t));
        log.Append(Event(t.AddSeconds(1)));
        log.Append(Event(t.AddSeconds(2)));

        int lines = File.ReadAllLines(_path).Count(l => !string.IsNullOrWhiteSpace(l));
        Assert.Equal(3, lines);
    }

    [Fact]
    public void On_Disk_Uses_The_Documented_Short_Keys()
    {
        // The JSONL key names are a documented contract (README §Data format).
        // Pin them so the format can't silently drift.
        var log = new NoiseLog(_path);
        log.Append(Event(new DateTime(2026, 7, 2, 14, 0, 0, DateTimeKind.Utc)));

        string line = File.ReadAllLines(_path)[0];
        Assert.Contains("\"t\":", line);
        Assert.Contains("\"pid\":", line);
        Assert.Contains("\"name\":", line);
        Assert.Contains("\"peak\":", line);
        Assert.Contains("\"session\":", line);
    }

    [Fact]
    public void Read_Tolerates_PascalCase_Hand_Edit()
    {
        // A user (or an older writer) using PascalCase keys must still load.
        File.WriteAllText(_path,
            "{\"T\":\"2026-07-02T14:00:00Z\",\"Pid\":7,\"Name\":\"vlc\",\"Peak\":0.9,\"Session\":\"VLC\"}\n");
        var e = new NoiseLog(_path).ReadAll();
        Assert.Single(e);
        Assert.Equal(7u, e[0].ProcessId);
        Assert.Equal("vlc", e[0].ProcessName);
    }
}
