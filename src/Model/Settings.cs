using System;

namespace NoiseSnitch.Model;

/// <summary>
/// User-tunable settings for the snitch, persisted between runs (M5). Kept as a
/// plain, mutable, parameterless-constructible type so <c>System.Text.Json</c>
/// can round-trip it without custom converters, and so a partially-written or
/// hand-edited file still deserializes (missing keys fall back to defaults).
///
/// Values are never trusted as-read: <see cref="Normalized"/> clamps everything
/// into safe ranges so a corrupt or hostile file can't wedge the app (e.g. a
/// zero poll interval spinning the timer, or a negative capacity throwing in
/// <see cref="NoiseSnitch.AudioWatcher.EventStore"/>). The clamping is pure and
/// unit-tested.
/// </summary>
internal sealed class Settings
{
    // --- Defaults (mirror the hard-coded M1–M4 constants so behaviour is
    //     unchanged until the user edits the file) ---

    /// <summary>Default poll cadence in ms (see <c>SessionWatcher.DefaultInterval</c>).</summary>
    public const int DefaultPollIntervalMs = 750;

    /// <summary>Default number of events retained (see <c>EventStore.DefaultCapacity</c>).</summary>
    public const int DefaultEventsToKeep = 200;

    /// <summary>Default onset peak floor (see <c>EdgeDetectorOptions.DefaultPeakThreshold</c>).</summary>
    public const float DefaultPeakThreshold = 0.015f;

    /// <summary>Default debounce/release in ms (see <c>EdgeDetectorOptions.DefaultReleaseTime</c>).</summary>
    public const int DefaultReleaseMs = 1000;

    // --- Clamp bounds. Generous but sane; the point is to stay usable, not to
    //     police taste. ---

    public const int MinPollIntervalMs = 100;    // faster than this just burns CPU
    public const int MaxPollIntervalMs = 60_000; // once a minute is the slow end

    public const int MinEventsToKeep = 10;
    public const int MaxEventsToKeep = 10_000;

    public const float MinPeakThreshold = 0f;    // 0 = "any active session counts"
    public const float MaxPeakThreshold = 1f;    // the meter is [0,1]

    public const int MinReleaseMs = 0;
    public const int MaxReleaseMs = 60_000;

    /// <summary>How often to poll audio sessions, in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = DefaultPollIntervalMs;

    /// <summary>How many recent events to retain in the blotter's ring buffer.</summary>
    public int EventsToKeep { get; set; } = DefaultEventsToKeep;

    /// <summary>Peak meter floor (<c>[0,1]</c>) a session must cross to count as sounding.</summary>
    public float PeakThreshold { get; set; } = DefaultPeakThreshold;

    /// <summary>Debounce window in ms: continuous quiet required before a new onset can fire.</summary>
    public int ReleaseMs { get; set; } = DefaultReleaseMs;

    /// <summary>A fresh instance carrying the built-in defaults.</summary>
    public static Settings Defaults() => new();

    /// <summary>
    /// Returns a copy with every field clamped into its valid range and any
    /// non-finite float coerced to the default. Pure: does not mutate <c>this</c>.
    /// Callers should feed the result (not the raw parsed object) to the runtime.
    /// </summary>
    public Settings Normalized() => new()
    {
        PollIntervalMs = Clamp(PollIntervalMs, MinPollIntervalMs, MaxPollIntervalMs, DefaultPollIntervalMs),
        EventsToKeep = Clamp(EventsToKeep, MinEventsToKeep, MaxEventsToKeep, DefaultEventsToKeep),
        PeakThreshold = ClampFloat(PeakThreshold, MinPeakThreshold, MaxPeakThreshold, DefaultPeakThreshold),
        ReleaseMs = Clamp(ReleaseMs, MinReleaseMs, MaxReleaseMs, DefaultReleaseMs),
    };

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value < min)
        {
            // A non-positive/absurd value is more likely a corrupt file than
            // intent; snap to the default rather than the floor for the two
            // fields where "min" is itself a real, usable choice.
            return value <= 0 ? fallback : min;
        }

        return value > max ? max : value;
    }

    private static float ClampFloat(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
