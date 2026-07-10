using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NoiseSnitch.Model;

namespace NoiseSnitch.AudioWatcher;

/// <summary>
/// The pure, UI-free brain behind the v0.2 "Per-app rules / allowlist" feature
/// (issue #9): given a user-curated set of process names to ignore, it decides
/// whether a given <see cref="NoiseEvent"/> should be suppressed — kept out of
/// the events feed and the blotter — so apps the user does not care about (their
/// music player, a game) never snitch.
///
/// Like <see cref="QuietHoursSchedule"/> and <see cref="EdgeDetector"/>, all the
/// matching logic lives here (free of WinForms and WASAPI) so the rules are pure
/// and deterministically unit-testable. The tray/watcher owns the live wiring;
/// it just asks this type "should I ignore this app?".
///
/// Matching is on the process name, compared <b>case-insensitively</b> and with
/// a trailing <c>.exe</c> stripped from both the stored rule and the event's
/// process name, so a user who types <c>Spotify</c>, <c>spotify</c>, or
/// <c>spotify.exe</c> all match the same app. Blank/whitespace rules are dropped.
/// This type is immutable and thread-safe.
/// </summary>
internal sealed class IgnoreList
{
    private readonly ImmutableHashSet<string> _ignored;

    /// <summary>
    /// Builds an ignore list from a raw sequence of process-name rules (as the
    /// user typed them). Each rule is normalized (trimmed, <c>.exe</c> stripped,
    /// lower-cased); blank rules are discarded. A <c>null</c> sequence is treated
    /// as empty (nothing ignored).
    /// </summary>
    public IgnoreList(IEnumerable<string>? rules)
    {
        var set = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        if (rules is not null)
        {
            foreach (var rule in rules)
            {
                var key = Normalize(rule);
                if (key.Length > 0)
                {
                    set.Add(key);
                }
            }
        }

        _ignored = set.ToImmutable();
    }

    /// <summary>An ignore list that suppresses nothing.</summary>
    public static IgnoreList Empty { get; } = new(null);

    /// <summary>How many distinct apps are being ignored.</summary>
    public int Count => _ignored.Count;

    /// <summary>The normalized (lower-cased, <c>.exe</c>-stripped) rules, sorted
    /// for stable display in a settings UI.</summary>
    public IReadOnlyList<string> Rules => _ignored.OrderBy(r => r, StringComparer.Ordinal).ToArray();

    /// <summary>True when the given raw process name is on the ignore list.</summary>
    public bool IsIgnored(string? processName) => _ignored.Contains(Normalize(processName));

    /// <summary>True when the given event's owning app is on the ignore list.</summary>
    public bool IsIgnored(NoiseEvent ev) => IsIgnored(ev.ProcessName);

    /// <summary>
    /// Filters a sequence of events, dropping any whose app is ignored. Order is
    /// preserved. Pure and lazy; callers materialize as needed.
    /// </summary>
    public IEnumerable<NoiseEvent> Filter(IEnumerable<NoiseEvent> events) =>
        events is null ? Enumerable.Empty<NoiseEvent>() : events.Where(ev => !IsIgnored(ev));

    /// <summary>
    /// Returns a copy with <paramref name="processName"/> added to the ignore
    /// list (the "ignore this app" action from a blotter row). A blank name is a
    /// no-op that returns an equivalent list. Pure: does not mutate <c>this</c>.
    /// </summary>
    public IgnoreList With(string? processName)
    {
        var key = Normalize(processName);
        if (key.Length == 0 || _ignored.Contains(key))
        {
            return this;
        }

        return new IgnoreList(_ignored.Add(key));
    }

    /// <summary>
    /// Returns a copy with <paramref name="processName"/> removed from the ignore
    /// list. A name that is not present is a no-op. Pure.
    /// </summary>
    public IgnoreList Without(string? processName)
    {
        var key = Normalize(processName);
        if (key.Length == 0 || !_ignored.Contains(key))
        {
            return this;
        }

        return new IgnoreList(_ignored.Remove(key));
    }

    /// <summary>
    /// Trim, drop a single trailing <c>.exe</c> (case-insensitive), and lower a
    /// process name to its canonical match key. Returns <c>""</c> for null/blank.
    /// </summary>
    private static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4].Trim();
        }

        return trimmed.ToLowerInvariant();
    }
}
