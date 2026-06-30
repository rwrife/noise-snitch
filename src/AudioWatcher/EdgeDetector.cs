using System;
using System.Collections.Generic;
using System.Linq;
using NoiseSnitch.Model;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// Tunables for <see cref="EdgeDetector"/>. Defaults are sensible for a ~750 ms
/// poll cadence (see <see cref="SessionWatcher.DefaultInterval"/>).
/// </summary>
/// <param name="PeakThreshold">
/// Minimum peak meter value (<c>[0, 1]</c>) for a session to count as "making
/// sound". A session can be in the active state yet effectively silent (peak ~0),
/// so we gate on the meter, not just the WASAPI state flag.
/// </param>
/// <param name="ReleaseTime">
/// How long a session must stay below <see cref="PeakThreshold"/> before a new
/// onset is allowed to fire again. This is the debounce: it collapses a
/// continuous (but jittery) stream into a single event and absorbs momentary
/// dips between words/notes without re-snitching.
/// </param>
internal readonly record struct EdgeDetectorOptions(
    float PeakThreshold,
    TimeSpan ReleaseTime)
{
    /// <summary>A small but non-zero floor; ~1.5% keeps meter noise from snitching.</summary>
    public const float DefaultPeakThreshold = 0.015f;

    /// <summary>Roughly a second of quiet collapses a continuous stream into one event.</summary>
    public static readonly TimeSpan DefaultReleaseTime = TimeSpan.FromMilliseconds(1000);

    public static EdgeDetectorOptions Default { get; } =
        new(DefaultPeakThreshold, DefaultReleaseTime);
}

/// <summary>
/// Turns a stream of per-tick <see cref="AudioSessionSnapshot"/> readings into
/// clean <see cref="NoiseEvent"/>s — one per silent → active onset, debounced.
///
/// This type is deliberately pure: it has no dependency on WASAPI/COM or any
/// clock. It is driven entirely by the snapshots handed to it (which carry their
/// own <see cref="AudioSessionSnapshot.TimestampUtc"/>), so its edge + debounce
/// behaviour is fully deterministic and unit-testable over fake snapshots.
///
/// Algorithm, per session (keyed by process id + session name):
/// <list type="bullet">
/// <item>A session is <i>sounding</i> when it is active and its peak is at or
/// above <see cref="EdgeDetectorOptions.PeakThreshold"/>.</item>
/// <item>A <see cref="NoiseEvent"/> fires when a session goes from
/// not-sounding → sounding.</item>
/// <item>After firing, further onsets are suppressed until the session has been
/// continuously not-sounding for at least
/// <see cref="EdgeDetectorOptions.ReleaseTime"/> (the debounce window). A brief
/// dip shorter than the release time does not re-arm the trigger.</item>
/// </list>
/// </summary>
internal sealed class EdgeDetector
{
    private readonly EdgeDetectorOptions _options;

    // Per-session state across ticks. Keyed by (pid, sessionName) so one process
    // with multiple sessions is tracked independently.
    private readonly Dictionary<(uint Pid, string Session), SessionState> _states = new();

    public EdgeDetector(EdgeDetectorOptions? options = null)
    {
        _options = options ?? EdgeDetectorOptions.Default;
    }

    /// <summary>
    /// Feeds one tick's worth of snapshots and returns any onsets detected this
    /// tick, in input order. Returns an empty list when nothing started making
    /// noise. Sessions absent from a tick are treated as silent for that tick
    /// (so a vanished session correctly re-arms after the release window).
    /// </summary>
    public IReadOnlyList<NoiseEvent> Process(IReadOnlyList<AudioSessionSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        List<NoiseEvent>? events = null;
        var seen = new HashSet<(uint, string)>();

        foreach (AudioSessionSnapshot s in snapshots)
        {
            var key = (s.ProcessId, s.SessionName ?? string.Empty);
            seen.Add(key);

            bool sounding = IsSounding(s);

            if (!_states.TryGetValue(key, out var state))
            {
                // Never seen before: treat as long-quiet so a session that is
                // already sounding on its first tick fires immediately (that IS
                // its onset). DateTime.MinValue makes Rearmed() trivially true.
                state = new SessionState { Sounding = false, SilentSince = DateTime.MinValue };
            }

            if (sounding)
            {
                // Fire only on a not-sounding → sounding edge, and only once the
                // session has been quiet long enough to have re-armed.
                if (!state.Sounding && Rearmed(state, s.TimestampUtc))
                {
                    (events ??= new List<NoiseEvent>()).Add(NoiseEvent.FromSnapshot(s));
                }

                state.Sounding = true;
                state.SilentSince = null;
            }
            else
            {
                if (state.Sounding)
                {
                    // Just dropped below threshold — start (or restart) the quiet clock.
                    state.SilentSince = s.TimestampUtc;
                }
                else
                {
                    // Still quiet; keep the earliest time we went quiet so the
                    // release window measures continuous silence.
                    state.SilentSince ??= s.TimestampUtc;
                }

                state.Sounding = false;
            }

            _states[key] = state;
        }

        // Any tracked session not present this tick counts as silent now. Mark it
        // not-sounding and stamp the quiet clock so it can re-arm. Snapshot the
        // keys first so we can safely reassign values while iterating.
        foreach (var key in _states.Keys.ToList())
        {
            if (seen.Contains(key))
            {
                continue;
            }

            var state = _states[key];
            if (state.Sounding)
            {
                state.Sounding = false;
                state.SilentSince ??= DateTime.MinValue; // unknown tick time; treat as long-quiet
            }

            _states[key] = state;
        }

        return (IReadOnlyList<NoiseEvent>?)events ?? Array.Empty<NoiseEvent>();
    }

    private bool IsSounding(AudioSessionSnapshot s) =>
        s.IsActive && s.PeakValue >= _options.PeakThreshold;

    /// <summary>
    /// True when the session has been continuously not-sounding for at least the
    /// release time (or has never sounded), so a fresh onset is allowed to fire.
    /// </summary>
    private bool Rearmed(SessionState state, DateTime now)
    {
        if (state.SilentSince is not { } since)
        {
            // No recorded quiet start means we were sounding right up to now;
            // not re-armed.
            return false;
        }

        return now - since >= _options.ReleaseTime;
    }

    /// <summary>Mutable per-session tracking state. Value type held in the dictionary.</summary>
    private struct SessionState
    {
        /// <summary>Whether the session was sounding as of the last tick we saw it.</summary>
        public bool Sounding;

        /// <summary>
        /// When the session most recently went (continuously) quiet, or
        /// <c>null</c> while it is currently sounding.
        /// </summary>
        public DateTime? SilentSince;
    }
}
