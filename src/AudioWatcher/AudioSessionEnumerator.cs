using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// Enumerates the current per-application audio render sessions on the default
/// playback device using NAudio's WASAPI wrappers
/// (<see cref="MMDeviceEnumerator"/> + <see cref="AudioSessionManager"/>), and
/// turns each one into an <see cref="AudioSessionSnapshot"/>.
///
/// M2 scope: just read the data (process name + peak + state). M3 will diff
/// these snapshots over time to detect silent → active transitions.
/// </summary>
internal sealed class AudioSessionEnumerator : IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();

    // Cache pid -> friendly name so we don't hit Process.GetProcessById every
    // tick for the same long-lived app. Cleared lazily when a pid no longer
    // resolves (the entry is replaced with an "(exited)" label).
    private readonly Dictionary<uint, string> _processNameCache = new();

    // Cache pid -> best-effort executable path (for the M5 blotter icon). Reading
    // Process.MainModule.FileName is comparatively expensive and can throw
    // (access denied / 32-vs-64-bit), so we resolve it once per pid. An empty
    // string is a cached "couldn't get it" so we don't retry every tick.
    private readonly Dictionary<uint, string> _executablePathCache = new();

    private bool _disposed;

    /// <summary>
    /// Takes a snapshot of every render session on the current default output
    /// device. Never throws for a single bad session — problem sessions are
    /// skipped and logged. Returns an empty list if no output device is present.
    /// </summary>
    public IReadOnlyList<AudioSessionSnapshot> Snapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var now = DateTime.UtcNow;
        var results = new List<AudioSessionSnapshot>();

        MMDevice? device = null;
        try
        {
            // No active playback device (e.g. headless CI / unplugged) -> nothing to see.
            if (!_deviceEnumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return results;
            }

            device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    results.Add(Capture(sessions[i], now));
                }
                catch (Exception ex)
                {
                    // A session can vanish mid-enumeration; that's expected churn.
                    DebugLog.Write($"[audio] skipped a session: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[audio] enumeration failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            device?.Dispose();
        }

        return results;
    }

    private AudioSessionSnapshot Capture(AudioSessionControl session, DateTime now)
    {
        uint pid = session.GetProcessID;
        float peak = session.AudioMeterInformation.MasterPeakValue;
        bool active = session.State == AudioSessionState.AudioSessionStateActive;

        // The shell pulls a friendly display name into the session sometimes;
        // it's frequently blank, which is fine.
        string sessionName = SafeDisplayName(session);
        string processName = ResolveProcessName(pid, session.IsSystemSoundsSession);
        string executablePath = ResolveExecutablePath(pid, session.IsSystemSoundsSession);

        return new AudioSessionSnapshot(now, pid, processName, sessionName, peak, active, executablePath);
    }

    private static string SafeDisplayName(AudioSessionControl session)
    {
        try
        {
            return session.DisplayName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Maps a session's process id to a friendly name, handling the system-sounds
    /// session (pid 0) and processes that have already exited.
    /// </summary>
    private string ResolveProcessName(uint pid, bool isSystemSounds)
    {
        if (isSystemSounds || pid == 0)
        {
            return "System Sounds";
        }

        if (_processNameCache.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        string name;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            name = string.IsNullOrWhiteSpace(proc.ProcessName)
                ? $"pid:{pid}"
                : proc.ProcessName;
        }
        catch (ArgumentException)
        {
            // Process is gone (exited between enumeration and lookup).
            name = $"pid:{pid} (exited)";
        }
        catch (InvalidOperationException)
        {
            name = $"pid:{pid} (exited)";
        }

        _processNameCache[pid] = name;
        return name;
    }

    /// <summary>
    /// Best-effort full path to a pid's main-module executable, for the M5
    /// blotter icon. Returns an empty string (never throws) for the system-sounds
    /// session, exited processes, or modules we're not allowed to read; the empty
    /// result is cached so we don't re-probe a locked-down process every tick.
    /// </summary>
    private string ResolveExecutablePath(uint pid, bool isSystemSounds)
    {
        if (isSystemSounds || pid == 0)
        {
            return string.Empty;
        }

        if (_executablePathCache.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        string path;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            path = proc.MainModule?.FileName ?? string.Empty;
        }
        catch (Exception ex)
        {
            // ArgumentException (gone), InvalidOperationException (no main module),
            // Win32Exception (access denied / bitness mismatch) — all expected and
            // non-fatal; the blotter just shows its generic glyph for this app.
            DebugLog.Write($"[audio] no exe path for pid {pid}: {ex.GetType().Name}");
            path = string.Empty;
        }

        _executablePathCache[pid] = path;
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deviceEnumerator.Dispose();
    }
}
