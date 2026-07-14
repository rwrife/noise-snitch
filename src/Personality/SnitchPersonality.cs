using System;

namespace NoiseSnitch.Personality;

/// <summary>
/// Issue #24: a "voice" for the snitch. A personality pack supplies the small set
/// of user-facing strings that give the app its character — the tray tooltip, the
/// blotter empty-state, and the phrasing for a caught onset — without touching any
/// UI or audio code. Everything here is pure and unit-testable.
///
/// Packs are immutable value objects. Look them up by their stable
/// <see cref="Key"/> (persisted in <c>Settings.PersonalityPack</c>); an unknown or
/// missing key falls back to <see cref="Default"/> via <see cref="Resolve"/>.
/// </summary>
internal sealed class SnitchPersonality
{
    /// <summary>The stable, lower-case identifier persisted in settings (e.g. "butler").</summary>
    public string Key { get; }

    /// <summary>A short human label for a settings/tray menu (e.g. "Polite Butler").</summary>
    public string DisplayName { get; }

    /// <summary>Text shown as the tray icon's tooltip when idle.</summary>
    public string TrayTooltip { get; }

    /// <summary>Text shown in the blotter when no events have been recorded yet.</summary>
    public string BlotterEmptyState { get; }

    private readonly string _eventTemplate;

    /// <param name="eventTemplate">
    /// Phrasing for a caught onset. Must contain a single <c>{0}</c> placeholder for
    /// the culprit's friendly name (e.g. <c>"caught {0} red-handed"</c>).
    /// </param>
    public SnitchPersonality(
        string key,
        string displayName,
        string trayTooltip,
        string blotterEmptyState,
        string eventTemplate)
    {
        Key = Normalize(key);
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        TrayTooltip = trayTooltip ?? throw new ArgumentNullException(nameof(trayTooltip));
        BlotterEmptyState = blotterEmptyState ?? throw new ArgumentNullException(nameof(blotterEmptyState));
        _eventTemplate = eventTemplate ?? throw new ArgumentNullException(nameof(eventTemplate));
    }

    /// <summary>
    /// Phrases a caught onset for the given (already friendly) culprit name, e.g.
    /// <c>"caught Google Chrome red-handed"</c>.
    /// </summary>
    public string PhraseEvent(string friendlyName) =>
        string.Format(_eventTemplate, friendlyName ?? string.Empty);

    /// <summary>Lower-cases and trims a key so lookups are case/whitespace insensitive.</summary>
    public static string Normalize(string? key) =>
        (key ?? string.Empty).Trim().ToLowerInvariant();
}
