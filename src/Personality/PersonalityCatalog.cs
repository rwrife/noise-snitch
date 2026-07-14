using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NoiseSnitch.Personality;

/// <summary>
/// Issue #24: the built-in registry of <see cref="SnitchPersonality"/> packs and
/// the lookup that maps a persisted key to a pack (with a safe fallback to the
/// default). Pure and UI-free.
/// </summary>
internal static class PersonalityCatalog
{
    /// <summary>The key of the pack used when none is chosen or the chosen one is unknown.</summary>
    public const string DefaultKey = "butler";

    private static readonly ReadOnlyCollection<SnitchPersonality> _packs = Build();

    private static ReadOnlyCollection<SnitchPersonality> Build()
    {
        var list = new List<SnitchPersonality>
        {
            // The default: courteous, understated. Sets a calm baseline.
            new SnitchPersonality(
                key: "butler",
                displayName: "Polite Butler",
                trayTooltip: "Noise Snitch — at your service, listening quietly.",
                blotterEmptyState: "All quiet, sir. Nothing to report… for now. 🤫",
                eventTemplate: "I regret to inform you that {0} broke the silence."),

            // The scene-stealer: gleeful, over-the-top tattling.
            new SnitchPersonality(
                key: "gremlin",
                displayName: "Tattletale Gremlin",
                trayTooltip: "heehee… Noise Snitch is WATCHING 👀",
                blotterEmptyState: "Nobody's made a peep yet. BORING. 😴",
                eventTemplate: "OOOH! Caught {0} red-handed! 🚨"),

            // The dry one: flat, factual, zero enthusiasm.
            new SnitchPersonality(
                key: "deadpan",
                displayName: "Deadpan",
                trayTooltip: "Noise Snitch. Monitoring audio.",
                blotterEmptyState: "No events.",
                eventTemplate: "{0} made a sound."),
        };

        return new ReadOnlyCollection<SnitchPersonality>(list);
    }

    /// <summary>All built-in packs, in menu order. Never empty; the first is the default.</summary>
    public static IReadOnlyList<SnitchPersonality> All => _packs;

    /// <summary>The default pack (guaranteed present).</summary>
    public static SnitchPersonality Default =>
        _packs.First(p => p.Key == DefaultKey);

    /// <summary>
    /// Resolves a persisted key to a pack. An unknown, empty, or <c>null</c> key
    /// (case/whitespace-insensitive) falls back to <see cref="Default"/>, so the
    /// runtime always gets a usable voice.
    /// </summary>
    public static SnitchPersonality Resolve(string? key)
    {
        string norm = SnitchPersonality.Normalize(key);
        return _packs.FirstOrDefault(p => p.Key == norm) ?? Default;
    }

    /// <summary>True if <paramref name="key"/> names a known built-in pack.</summary>
    public static bool IsKnown(string? key)
    {
        string norm = SnitchPersonality.Normalize(key);
        return _packs.Any(p => p.Key == norm);
    }
}
