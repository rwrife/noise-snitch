using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoiseSnitch.Model;

/// <summary>
/// A parsed global-hotkey combination (issue #28) — a set of modifier keys plus a
/// single main key — expressed in the two integer forms the Win32
/// <c>RegisterHotKey</c> API consumes: <see cref="Modifiers"/> (the
/// <c>fsModifiers</c> bitmask) and <see cref="VirtualKey"/> (the target virtual-key
/// code). Kept as a small immutable value with <b>pure</b> parsing/formatting so the
/// "what does <c>Ctrl+Alt+N</c> mean" rule lives in one unit-tested place that
/// <see cref="Settings"/> normalization and the tray's registration both share —
/// mirroring how <see cref="TimeOfDayText"/> owns the quiet-hours string.
///
/// The string form is the hand-editable representation in <c>settings.json</c>:
/// a <c>+</c>-separated list of modifier tokens followed by exactly one main key,
/// e.g. <c>"Ctrl+Alt+N"</c>, <c>"Ctrl+Shift+F1"</c>. Parsing is forgiving of case,
/// surrounding whitespace, and common aliases (<c>Control</c>, <c>Win</c>, …), but
/// rejects genuine nonsense (no main key, an unknown token, a lone modifier) by
/// falling back to the caller-supplied default — because a typo should not silently
/// leave the app with no way to pop the blotter.
/// </summary>
internal sealed class Hotkey : IEquatable<Hotkey>
{
    // --- Win32 fsModifiers bits (MOD_*), see RegisterHotKey docs. ---
    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int ModWin = 0x0008;
    // MOD_NOREPEAT (0x4000) is OR'd in at registration time so holding the combo
    // doesn't machine-gun the message; it isn't part of the user's logical combo,
    // so it's intentionally not stored here.

    /// <summary>The default combo when settings are missing/unparseable: <c>Ctrl+Alt+N</c>.</summary>
    public const string DefaultText = "Ctrl+Alt+N";

    /// <summary>The <c>fsModifiers</c> bitmask (a combination of the <c>Mod*</c> flags).</summary>
    public int Modifiers { get; }

    /// <summary>The virtual-key code of the (single) main key.</summary>
    public int VirtualKey { get; }

    private Hotkey(int modifiers, int virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    /// <summary>
    /// Parses a <c>+</c>-separated combo string into a <see cref="Hotkey"/>, or
    /// returns <paramref name="fallback"/> when it can't. A valid combo needs at
    /// least one modifier and exactly one main key — a bare key (no modifier) is
    /// rejected because a global hotkey with no modifier would hijack a plain
    /// keystroke system-wide. Case-, whitespace-, and alias-insensitive.
    /// </summary>
    public static Hotkey Parse(string? text, Hotkey fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return TryParse(text, out Hotkey? parsed) ? parsed! : fallback;
    }

    /// <summary>
    /// Attempts to parse a combo string. Returns <c>false</c> (and a <c>null</c>
    /// out) on any problem: empty, no main key, a lone/duplicate-only modifier set,
    /// an unknown token, or more than one main key.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int modifiers = 0;
        int? vk = null;

        foreach (string rawToken in text.Split('+'))
        {
            string token = rawToken.Trim();
            if (token.Length == 0)
            {
                // A stray "Ctrl++N" or trailing "+" — treat as malformed.
                return false;
            }

            string key = token.ToUpperInvariant();
            if (ModifierBits.TryGetValue(key, out int bit))
            {
                // OR-ing means "Ctrl+Ctrl+N" collapses harmlessly to one Ctrl.
                modifiers |= bit;
                continue;
            }

            if (!MainKeys.TryGetValue(key, out int code))
            {
                return false; // unknown token
            }

            if (vk is not null)
            {
                return false; // more than one main key
            }

            vk = code;
        }

        if (vk is null || modifiers == 0)
        {
            // Need a main key AND at least one modifier for a safe global hotkey.
            return false;
        }

        hotkey = new Hotkey(modifiers, vk.Value);
        return true;
    }

    /// <summary>
    /// Renders back to the canonical <c>"Ctrl+Alt+Shift+Win+KEY"</c> string with
    /// modifiers in a stable order, so a round-trip through parse → format is
    /// idempotent and the persisted file is clean regardless of how the user typed
    /// it.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(MainKeyName(VirtualKey));
        return string.Join("+", parts);
    }

    /// <summary>The built-in default combo, materialized. Never null.</summary>
    public static Hotkey Default() => Parse(DefaultText, MinimalDefault());

    /// <summary>
    /// Convenience overload used by callers that already hold a default string but
    /// want the guaranteed-non-null default combo as the fallback.
    /// </summary>
    public static Hotkey Parse(string? text) => Parse(text, MinimalDefault());

    // A hard-coded, always-valid Ctrl+Alt+N so Default()/Parse(text) can never
    // recurse into needing another fallback.
    private static Hotkey MinimalDefault() => new(ModControl | ModAlt, VkN);

    private const int VkN = 0x4E; // 'N'

    private static string MainKeyName(int virtualKey)
    {
        // Reverse-lookup the canonical name; fall back to a hex code so an exotic
        // key still round-trips to *something* parseable-adjacent rather than "".
        foreach (var kv in MainKeys)
        {
            if (kv.Value == virtualKey)
            {
                return Capitalize(kv.Key);
            }
        }

        return "0x" + virtualKey.ToString("X2", CultureInfo.InvariantCulture);
    }

    private static string Capitalize(string upper) =>
        upper.Length <= 1 ? upper : upper[0] + upper.Substring(1).ToLowerInvariant();

    // Modifier aliases → fsModifiers bit.
    private static readonly Dictionary<string, int> ModifierBits = new()
    {
        ["CTRL"] = ModControl,
        ["CONTROL"] = ModControl,
        ["CTL"] = ModControl,
        ["ALT"] = ModAlt,
        ["SHIFT"] = ModShift,
        ["WIN"] = ModWin,
        ["WINDOWS"] = ModWin,
        ["META"] = ModWin,
        ["SUPER"] = ModWin,
        ["CMD"] = ModWin,
    };

    // Supported main keys → virtual-key code. Letters, digits, F1–F24, and a few
    // common extras. Enough to build any sane blotter-pop combo; unknown keys are
    // rejected (fallback) rather than guessed.
    private static readonly Dictionary<string, int> MainKeys = BuildMainKeys();

    private static Dictionary<string, int> BuildMainKeys()
    {
        var map = new Dictionary<string, int>();

        // A–Z share their ASCII uppercase code as the VK.
        for (char c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = c;
        }

        // 0–9 (top row) share their ASCII code.
        for (char c = '0'; c <= '9'; c++)
        {
            map[c.ToString()] = c;
        }

        // F1–F24 → VK_F1 (0x70) .. VK_F24 (0x87).
        for (int i = 1; i <= 24; i++)
        {
            map["F" + i.ToString(CultureInfo.InvariantCulture)] = 0x70 + (i - 1);
        }

        // A few frequently-wanted extras.
        map["SPACE"] = 0x20;      // VK_SPACE
        map["TAB"] = 0x09;        // VK_TAB
        map["ENTER"] = 0x0D;      // VK_RETURN
        map["RETURN"] = 0x0D;
        map["ESC"] = 0x1B;        // VK_ESCAPE
        map["ESCAPE"] = 0x1B;
        map["HOME"] = 0x24;       // VK_HOME
        map["END"] = 0x23;        // VK_END
        map["INSERT"] = 0x2D;     // VK_INSERT
        map["DELETE"] = 0x2E;     // VK_DELETE
        map["DEL"] = 0x2E;

        return map;
    }

    public bool Equals(Hotkey? other) =>
        other is not null && Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;

    public override bool Equals(object? obj) => Equals(obj as Hotkey);

    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
}
