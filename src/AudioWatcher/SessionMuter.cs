using System;
using NAudio.CoreAudioApi;
using NoiseSnitch.Diagnostics;
using NoiseSnitch.Model;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// The v0.2 "Mute-the-snitched" muscle (issue #7): given the process id captured
/// on a blotter entry, find that app's <em>live</em> render session(s) on the
/// current default output device and toggle their mute state via
/// <see cref="SimpleAudioVolume"/> — "EarTrumpet-lite," one click from the row
/// that snitched.
///
/// It re-enumerates on demand (the <see cref="NoiseEvent"/> only carries a pid, a
/// name and a peak — never a live COM handle, which would be long dead by the
/// time you click), and, like <see cref="AudioSessionEnumerator"/>, is defensive:
/// a session can vanish mid-operation, so nothing here throws — every path
/// returns a <see cref="MuteOutcome"/> the UI can render.
///
/// Windows applies mute to a whole audio <em>session</em>; a process can own more
/// than one (e.g. a browser per-tab), so we toggle <b>all</b> sessions for the
/// pid together and report the resulting state.
///
/// Touches WASAPI COM, so — like the rest of the audio layer — it's meant to be
/// driven from the app's single STA/UI thread.
/// </summary>
internal sealed class SessionMuter : IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private bool _disposed;

    /// <summary>
    /// Reads whether the given process's live session(s) are currently muted.
    /// Returns <see cref="MuteOutcome.Muted"/> / <see cref="MuteOutcome.Unmuted"/>
    /// to reflect state (if it owns several sessions, muted-if-any so the UI errs
    /// toward showing the app as silenced), <see cref="MuteOutcome.NoSession"/>
    /// when the app has no live session, or <see cref="MuteOutcome.Failed"/> on a
    /// read error. Never throws.
    /// </summary>
    public MuteOutcome QueryMuted(uint processId)
    {
        if (processId == 0)
        {
            return MuteOutcome.SystemSoundsDeclined;
        }

        return WithSessionsForPid(
            processId,
            onEach: null,
            afterMatched: anyMuted => anyMuted ? MuteOutcome.Muted : MuteOutcome.Unmuted);
    }

    /// <summary>
    /// Sets the mute state of every live session owned by <paramref name="processId"/>
    /// to <paramref name="mute"/>. Returns <see cref="MuteOutcome.Muted"/> /
    /// <see cref="MuteOutcome.Unmuted"/> on success, <see cref="MuteOutcome.NoSession"/>
    /// if the app has no live session to act on, or <see cref="MuteOutcome.Failed"/>
    /// if a matching session was found but couldn't be changed. Never throws.
    /// </summary>
    public MuteOutcome SetMuted(uint processId, bool mute)
    {
        if (processId == 0)
        {
            return MuteOutcome.SystemSoundsDeclined;
        }

        bool anyFailed = false;

        MuteOutcome outcome = WithSessionsForPid(
            processId,
            onEach: session =>
            {
                try
                {
                    session.SimpleAudioVolume.Mute = mute;
                }
                catch (Exception ex)
                {
                    anyFailed = true;
                    DebugLog.Write($"[mute] set failed for pid {processId}: {ex.GetType().Name}: {ex.Message}");
                }
            },
            afterMatched: _ => mute ? MuteOutcome.Muted : MuteOutcome.Unmuted);

        // If we matched sessions but every attempt to flip one threw, report Failed
        // rather than falsely claiming the new state.
        if (outcome is MuteOutcome.Muted or MuteOutcome.Unmuted && anyFailed)
        {
            return MuteOutcome.Failed;
        }

        return outcome;
    }

    /// <summary>
    /// Convenience for the blotter's single toggle click: read the current state
    /// and flip it. Returns the resulting <see cref="MuteOutcome"/>. If the app
    /// has no live session, reports <see cref="MuteOutcome.NoSession"/>.
    /// </summary>
    public MuteOutcome Toggle(uint processId)
    {
        MuteOutcome current = QueryMuted(processId);
        return current switch
        {
            MuteOutcome.Muted => SetMuted(processId, false),
            MuteOutcome.Unmuted => SetMuted(processId, true),
            // NoSession / SystemSoundsDeclined / Failed: pass straight through —
            // there's nothing to flip.
            _ => current,
        };
    }

    /// <summary>
    /// Shared enumeration core: walk the current default render device's sessions,
    /// invoke <paramref name="onEach"/> for every session whose pid matches
    /// (skipping the system-sounds session), and, if at least one matched, return
    /// <paramref name="afterMatched"/> applied to "was any matched session muted?".
    /// Returns <see cref="MuteOutcome.NoSession"/> when nothing matched and
    /// <see cref="MuteOutcome.Failed"/> if enumeration itself blew up. Never throws.
    /// </summary>
    private MuteOutcome WithSessionsForPid(
        uint processId,
        Action<AudioSessionControl>? onEach,
        Func<bool, MuteOutcome> afterMatched)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        MMDevice? device = null;
        try
        {
            if (!_deviceEnumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return MuteOutcome.NoSession;
            }

            device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            bool matchedAny = false;
            bool anyMuted = false;

            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionControl session;
                try
                {
                    session = sessions[i];
                }
                catch (Exception ex)
                {
                    // A session can vanish mid-enumeration; expected churn, skip it.
                    DebugLog.Write($"[mute] skipped a session: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                using (session)
                {
                    if (!SessionMatchesPid(session, processId))
                    {
                        continue;
                    }

                    matchedAny = true;
                    onEach?.Invoke(session);

                    try
                    {
                        anyMuted |= session.SimpleAudioVolume.Mute;
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"[mute] read state failed for pid {processId}: {ex.GetType().Name}");
                    }
                }
            }

            return matchedAny ? afterMatched(anyMuted) : MuteOutcome.NoSession;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[mute] enumeration failed for pid {processId}: {ex.GetType().Name}: {ex.Message}");
            return MuteOutcome.Failed;
        }
        finally
        {
            device?.Dispose();
        }
    }

    /// <summary>
    /// True if this session belongs to <paramref name="processId"/> and is not the
    /// shared system-sounds session. Reading pid / the system-sounds flag can throw
    /// on an unsupported OS or a dying session, so treat any failure as "no match".
    /// </summary>
    private static bool SessionMatchesPid(AudioSessionControl session, uint processId)
    {
        try
        {
            if (session.IsSystemSoundsSession)
            {
                return false;
            }

            return session.GetProcessID == processId;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[mute] pid probe failed: {ex.GetType().Name}");
            return false;
        }
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
