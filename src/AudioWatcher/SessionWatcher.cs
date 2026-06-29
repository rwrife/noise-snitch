using System;
using System.Windows.Forms;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// Drives <see cref="AudioSessionEnumerator"/> on a fixed interval and dumps each
/// per-session reading to the debug log. This is the M2 "prove the data flows"
/// loop: every tick we log <c>{time, process, peak, sessionName}</c> for every
/// active render session.
///
/// Uses a WinForms <see cref="System.Windows.Forms.Timer"/> so ticks marshal
/// onto the UI thread that owns the tray app; the underlying WASAPI COM objects
/// are simplest to touch from a single STA thread. M3 will replace the raw dump
/// with edge detection that raises a <c>NoiseEvent</c> only on silent → active
/// transitions.
/// </summary>
internal sealed class SessionWatcher : IDisposable
{
    /// <summary>Default poll cadence. Fast enough to feel live, cheap enough to ignore.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(750);

    private readonly AudioSessionEnumerator _enumerator = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    private bool _started;
    private bool _disposed;

    public SessionWatcher(TimeSpan? interval = null)
    {
        _timer.Interval = (int)(interval ?? DefaultInterval).TotalMilliseconds;
        _timer.Tick += OnTick;
    }

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
        if (snapshots.Count == 0)
        {
            return;
        }

        // Dump every session this tick. Idle sessions are included too so we can
        // see the full picture while bringing M2 up; M3 narrows this to edges.
        foreach (AudioSessionSnapshot s in snapshots)
        {
            DebugLog.Write(
                $"[audio] {s.TimestampUtc:O} {s.ProcessName} " +
                $"peak={s.PeakValue:0.000} " +
                $"state={(s.IsActive ? "active" : "idle")}" +
                (string.IsNullOrWhiteSpace(s.SessionName) ? "" : $" session=\"{s.SessionName}\""));
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
