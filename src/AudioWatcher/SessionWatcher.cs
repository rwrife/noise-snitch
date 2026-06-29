using System;
using System.Windows.Forms;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// Drives <see cref="AudioSessionEnumerator"/> on a fixed interval, runs each
/// tick's readings through the <see cref="EdgeDetector"/>, and records the
/// resulting <see cref="NoiseEvent"/>s into an <see cref="EventStore"/> (also
/// logging each onset).
///
/// This is the M3 loop: instead of dumping every session every tick (M2), we now
/// emit a clean event only when an app *starts* making sound, debounced so a
/// continuous stream snitches once rather than every tick. The blotter UI (M4)
/// reads <see cref="Events"/>.
///
/// Uses a WinForms <see cref="System.Windows.Forms.Timer"/> so ticks marshal
/// onto the UI thread that owns the tray app; the underlying WASAPI COM objects
/// are simplest to touch from a single STA thread.
/// </summary>
internal sealed class SessionWatcher : IDisposable
{
    /// <summary>Default poll cadence. Fast enough to feel live, cheap enough to ignore.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(750);

    private readonly AudioSessionEnumerator _enumerator = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly EdgeDetector _detector;
    private readonly EventStore _events;

    private bool _started;
    private bool _disposed;

    public SessionWatcher(
        TimeSpan? interval = null,
        EdgeDetectorOptions? detectorOptions = null,
        EventStore? events = null)
    {
        _timer.Interval = (int)(interval ?? DefaultInterval).TotalMilliseconds;
        _timer.Tick += OnTick;
        _detector = new EdgeDetector(detectorOptions);
        _events = events ?? new EventStore();
    }

    /// <summary>The recent-events history the blotter (M4) renders.</summary>
    public EventStore Events => _events;

    /// <summary>Begins polling. Idempotent.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        DebugLog.Write(
            $"[audio] watcher started (interval={_timer.Interval}ms). " +
            $"Log file: {DebugLog.FilePath ?? "<trace only>"}");
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var snapshots = _enumerator.Snapshot();

        // Always run the detector — even an empty tick matters, because absent
        // sessions are how a stopped stream re-arms for its next onset.
        foreach (NoiseEvent ev in _detector.Process(snapshots))
        {
            _events.Add(ev);
            DebugLog.Write($"[noise] {ev.TimestampUtc:O} {ev}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Tick -= OnTick;
        _timer.Dispose();
        _enumerator.Dispose();
    }
}
