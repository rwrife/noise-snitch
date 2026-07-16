using System;
using System.Linq;

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

    /// <summary>
    /// Default for the on-disk noise log (M6): <b>off</b>. The snitch is
    /// local-only and privacy-conscious, so durable history is strictly opt-in —
    /// nothing is written to disk until the user flips this on.
    /// </summary>
    public const bool DefaultPersistLog = false;

    /// <summary>
    /// Default rolling-log size cap in bytes (M6): 5 MiB. At ~120 bytes/line that
    /// is tens of thousands of events — plenty for "what happened this week" —
    /// before the oldest half is rotated out.
    /// </summary>
    public const long DefaultMaxLogBytes = 5L * 1024 * 1024;

    /// <summary>
    /// Default for quiet-hours alerting (issue #8): <b>off</b>. The window only
    /// escalates onsets once the user opts in and picks their focus hours.
    /// </summary>
    public const bool DefaultQuietHoursEnabled = false;

    /// <summary>
    /// Default quiet-window start, <c>"22:00"</c> — a sensible "evening wind-down"
    /// anchor so the materialized file shows a realistic overnight example the
    /// moment the user flips <see cref="QuietHoursEnabled"/> on.
    /// </summary>
    public const string DefaultQuietHoursStart = "22:00";

    /// <summary>Default quiet-window end, <c>"07:00"</c> (pairs with the 22:00 start).</summary>
    public const string DefaultQuietHoursEnd = "07:00";

    /// <summary>
    /// Issue #9: the default per-app ignore list is <b>empty</b> — the snitch
    /// watches every app until the user explicitly silences one.
    /// </summary>
    public static string[] DefaultIgnoredApps() => Array.Empty<string>();

    /// <summary>
    /// Issue #24: default snitch personality pack key. Mirrors
    /// <see cref="NoiseSnitch.Personality.PersonalityCatalog.DefaultKey"/> so an
    /// un-edited file uses the courteous "butler" voice.
    /// </summary>
    public const string DefaultPersonalityPack =
        NoiseSnitch.Personality.PersonalityCatalog.DefaultKey;

    /// <summary>
    /// Issue #28: global-hotkey feature is <b>on</b> by default — popping the
    /// blotter from anywhere is the whole point, and it degrades gracefully if the
    /// combo is already taken (logged, no crash).
    /// </summary>
    public const bool DefaultHotkeyEnabled = true;

    /// <summary>
    /// Issue #28: default global hotkey to pop the blotter, mirroring
    /// <see cref="NoiseSnitch.Model.Hotkey.DefaultText"/> (<c>Ctrl+Alt+N</c>).
    /// </summary>
    public const string DefaultHotkeyCombo = Hotkey.DefaultText;

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

    // A floor keeps rotation meaningful (never so tiny a single event can't fit
    // twice); the ceiling stops a hand-edit from letting the log grow unbounded.
    public const long MinMaxLogBytes = 64L * 1024;          // 64 KiB
    public const long MaxMaxLogBytes = 1024L * 1024 * 1024; // 1 GiB

    /// <summary>How often to poll audio sessions, in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = DefaultPollIntervalMs;

    /// <summary>How many recent events to retain in the blotter's ring buffer.</summary>
    public int EventsToKeep { get; set; } = DefaultEventsToKeep;

    /// <summary>Peak meter floor (<c>[0,1]</c>) a session must cross to count as sounding.</summary>
    public float PeakThreshold { get; set; } = DefaultPeakThreshold;

    /// <summary>Debounce window in ms: continuous quiet required before a new onset can fire.</summary>
    public int ReleaseMs { get; set; } = DefaultReleaseMs;

    /// <summary>
    /// M6: when <c>true</c>, each onset is appended to a rolling JSONL log under
    /// <c>%LOCALAPPDATA%\noise-snitch\noise-log.jsonl</c> so history survives
    /// restarts. Off by default (local-only, opt-in).
    /// </summary>
    public bool PersistLog { get; set; } = DefaultPersistLog;

    /// <summary>M6: size cap in bytes for the rolling on-disk log before it rotates.</summary>
    public long MaxLogBytes { get; set; } = DefaultMaxLogBytes;

    /// <summary>
    /// Issue #8: when <c>true</c>, onsets that occur inside the quiet window are
    /// escalated (a loud tray toast) on top of the usual flash, so a sound during
    /// your focus/sleep hours is hard to miss. Off by default.
    /// </summary>
    public bool QuietHoursEnabled { get; set; } = DefaultQuietHoursEnabled;

    /// <summary>
    /// Issue #8: inclusive start of the quiet window as local wall-clock
    /// <c>"HH:mm"</c> (24-hour). Hand-editable; an unparseable value falls back to
    /// <see cref="DefaultQuietHoursStart"/> during <see cref="Normalized"/>.
    /// </summary>
    public string QuietHoursStart { get; set; } = DefaultQuietHoursStart;

    /// <summary>
    /// Issue #8: exclusive end of the quiet window as local wall-clock
    /// <c>"HH:mm"</c> (24-hour). A window whose end is <em>earlier</em> than its
    /// start wraps past midnight (e.g. 22:00 → 07:00).
    /// </summary>
    public string QuietHoursEnd { get; set; } = DefaultQuietHoursEnd;

    /// <summary>
    /// The normalized inclusive window start as a minute-of-day (<c>[0, 1440)</c>),
    /// parsed from <see cref="QuietHoursStart"/> with the default as fallback.
    /// This is what the runtime schedule consumes; the string is just the
    /// human-editable form. Not serialized (computed).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int QuietHoursStartMinute =>
        TimeOfDayText.ParseToMinuteOfDay(
            QuietHoursStart, TimeOfDayText.ParseToMinuteOfDay(DefaultQuietHoursStart, 22 * 60));

    /// <summary>
    /// The normalized exclusive window end as a minute-of-day (<c>[0, 1440)</c>),
    /// parsed from <see cref="QuietHoursEnd"/> with the default as fallback. Not
    /// serialized (computed).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int QuietHoursEndMinute =>
        TimeOfDayText.ParseToMinuteOfDay(
            QuietHoursEnd, TimeOfDayText.ParseToMinuteOfDay(DefaultQuietHoursEnd, 7 * 60));

    /// <summary>
    /// Issue #9: process names the user has chosen to ignore. Events from these
    /// apps are filtered out of the feed and blotter (see
    /// <see cref="NoiseSnitch.AudioWatcher.IgnoreList"/>). Rules are matched
    /// case-insensitively with a trailing <c>.exe</c> stripped, so <c>Spotify</c>,
    /// <c>spotify</c>, and <c>spotify.exe</c> all mean the same app. A missing or
    /// <c>null</c> value in the file deserializes to the empty list (nothing
    /// ignored).
    /// </summary>
    public string[] IgnoredApps { get; set; } = DefaultIgnoredApps();

    /// <summary>
    /// Issue #24: key of the selected personality pack (the snitch's "voice" across
    /// the tray tooltip, blotter empty-state, and event phrasing). Matched
    /// case-insensitively; an unknown or missing value falls back to
    /// <see cref="DefaultPersonalityPack"/> during <see cref="Normalized"/>.
    /// </summary>
    public string PersonalityPack { get; set; } = DefaultPersonalityPack;

    /// <summary>
    /// Issue #28: when <c>true</c>, a system-wide hotkey (see
    /// <see cref="HotkeyCombo"/>) toggles the blotter flyout from anywhere. On by
    /// default; a registration clash just logs and disables the shortcut for the
    /// session without affecting anything else.
    /// </summary>
    public bool HotkeyEnabled { get; set; } = DefaultHotkeyEnabled;

    /// <summary>
    /// Issue #28: the global-hotkey combo as a hand-editable <c>+</c>-separated
    /// string (e.g. <c>"Ctrl+Alt+N"</c>). Parsed/canonicalized via the pure
    /// <see cref="Hotkey"/> helper; an unparseable value snaps back to
    /// <see cref="DefaultHotkeyCombo"/> during <see cref="Normalized"/>.
    /// </summary>
    public string HotkeyCombo { get; set; } = DefaultHotkeyCombo;

    /// <summary>
    /// The parsed global hotkey, resolved from <see cref="HotkeyCombo"/> with the
    /// built-in default as fallback. Not serialized (computed); the string is the
    /// human-editable form the runtime registration consumes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Hotkey Hotkey => NoiseSnitch.Model.Hotkey.Parse(HotkeyCombo);

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
        PersistLog = PersistLog, // a bool needs no clamping
        MaxLogBytes = ClampLong(MaxLogBytes, MinMaxLogBytes, MaxMaxLogBytes, DefaultMaxLogBytes),
        QuietHoursEnabled = QuietHoursEnabled, // a bool needs no clamping
        // Canonicalize the window strings via the pure parser: a valid "9:5"
        // becomes "09:05", and anything unparseable snaps back to the default so
        // the persisted file always holds a well-formed "HH:mm".
        QuietHoursStart = TimeOfDayText.FromMinuteOfDay(QuietHoursStartMinute),
        QuietHoursEnd = TimeOfDayText.FromMinuteOfDay(QuietHoursEndMinute),
        // Canonicalize the ignore list via the same rules the runtime uses:
        // trim, drop a trailing ".exe", lower-case, and de-dupe blanks/repeats so
        // the persisted file holds a clean, stable set.
        IgnoredApps = new NoiseSnitch.AudioWatcher.IgnoreList(IgnoredApps).Rules.ToArray(),
        // Canonicalize the personality key: normalize case/whitespace and snap an
        // unknown pack back to the default so the persisted file always names a
        // real voice the runtime can resolve.
        PersonalityPack = NoiseSnitch.Personality.PersonalityCatalog
            .Resolve(PersonalityPack).Key,
        HotkeyEnabled = HotkeyEnabled, // a bool needs no clamping
        // Canonicalize the combo string via the pure parser: aliases/case/spacing
        // are normalized and anything unparseable snaps back to the default so the
        // persisted file always holds a well-formed, registerable combo.
        HotkeyCombo = NoiseSnitch.Model.Hotkey.Parse(HotkeyCombo).ToString(),
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

    private static long ClampLong(long value, long min, long max, long fallback)
    {
        // A non-positive/absurd size is more likely a corrupt file than intent;
        // snap to the default rather than the floor.
        if (value <= 0)
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
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
