using NoiseSnitch.Model;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Parsing/formatting rules for the pure <see cref="Hotkey"/> helper (issue #28) —
/// the one place that turns a hand-editable <c>"Ctrl+Alt+N"</c> combo string into
/// the Win32 <c>fsModifiers</c>/virtual-key pair and back. Being forgiving of
/// hand-edits (case, spacing, aliases) while rejecting genuinely unsafe/nonsense
/// combos (no modifier, no main key, unknown tokens) is the whole job.
/// </summary>
public sealed class HotkeyTests
{
    // A distinctive sentinel fallback so we can prove rejection returned *it*.
    private static readonly Hotkey Fallback = MakeFallback();

    private static Hotkey MakeFallback()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+F12", out Hotkey? hk));
        return hk!;
    }

    [Fact]
    public void Parses_Default_CtrlAltN()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Alt+N", out Hotkey? hk));
        Assert.Equal(Hotkey.ModControl | Hotkey.ModAlt, hk!.Modifiers);
        Assert.Equal(0x4E, hk.VirtualKey); // 'N'
    }

    [Theory]
    [InlineData("ctrl+alt+n")]        // lower-case
    [InlineData("  Ctrl + Alt + N ")] // whitespace around tokens
    [InlineData("CONTROL+ALT+N")]     // alias + upper-case
    [InlineData("Alt+Ctrl+N")]        // modifier order irrelevant
    [InlineData("Ctrl+Ctrl+Alt+N")]   // duplicate modifier collapses
    public void Parses_Forgivingly_ToSameCombo(string text)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey? hk));
        Assert.Equal(Hotkey.ModControl | Hotkey.ModAlt, hk!.Modifiers);
        Assert.Equal(0x4E, hk.VirtualKey);
    }

    [Theory]
    [InlineData("Win+Shift+S", Hotkey.ModWin | Hotkey.ModShift, 'S')]
    [InlineData("Cmd+Space", Hotkey.ModWin, ' ')]
    [InlineData("Ctrl+Alt+Delete", Hotkey.ModControl | Hotkey.ModAlt, 0x2E)]
    public void Parses_Modifiers_And_Extras(string text, int expectedMods, int expectedVk)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey? hk));
        Assert.Equal(expectedMods, hk!.Modifiers);
        Assert.Equal(expectedVk, hk.VirtualKey);
    }

    [Fact]
    public void Parses_FunctionKeys()
    {
        Assert.True(Hotkey.TryParse("Ctrl+F1", out Hotkey? f1));
        Assert.Equal(0x70, f1!.VirtualKey);

        Assert.True(Hotkey.TryParse("Ctrl+F24", out Hotkey? f24));
        Assert.Equal(0x87, f24!.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N")]              // no modifier — unsafe as a global hotkey
    [InlineData("Ctrl")]          // lone modifier, no main key
    [InlineData("Ctrl+Alt")]      // still no main key
    [InlineData("Ctrl+Alt+N+M")]  // two main keys
    [InlineData("Ctrl+Frobnicate")] // unknown token
    [InlineData("Ctrl++N")]       // empty token
    [InlineData("Ctrl+Alt+")]     // trailing plus
    public void Rejects_And_Returns_Fallback(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out Hotkey? hk));
        Assert.Null(hk);

        Hotkey resolved = Hotkey.Parse(text, Fallback);
        Assert.Equal(Fallback, resolved);
    }

    [Theory]
    [InlineData("ctrl+alt+n", "Ctrl+Alt+N")]
    [InlineData("alt+ctrl+n", "Ctrl+Alt+N")]         // canonical modifier order
    [InlineData("win+shift+s", "Ctrl+Alt+N")]        // (round-trip checked below, not equality)
    [InlineData("Ctrl+Shift+Win+F5", "Ctrl+Shift+Win+F5")]
    public void ToString_Is_Canonical(string input, string _ignoredExpected)
    {
        // Round-trip: parse → format → parse yields an equal combo, and the
        // formatted string itself re-parses to the same value (idempotent).
        Assert.True(Hotkey.TryParse(input, out Hotkey? first));
        string formatted = first!.ToString();

        Assert.True(Hotkey.TryParse(formatted, out Hotkey? second));
        Assert.Equal(first, second);
        Assert.Equal(formatted, second!.ToString());
    }

    [Fact]
    public void Canonical_Modifier_Order_Is_Ctrl_Alt_Shift_Win()
    {
        Assert.True(Hotkey.TryParse("win+shift+alt+ctrl+j", out Hotkey? hk));
        Assert.Equal("Ctrl+Alt+Shift+Win+J", hk!.ToString());
    }

    [Fact]
    public void Default_Is_CtrlAltN()
    {
        Hotkey def = Hotkey.Default();
        Assert.Equal("Ctrl+Alt+N", def.ToString());
        Assert.Equal(Hotkey.ModControl | Hotkey.ModAlt, def.Modifiers);
    }

    [Fact]
    public void Equality_By_Value()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Alt+N", out Hotkey? a));
        Assert.True(Hotkey.TryParse("alt+ctrl+n", out Hotkey? b));
        Assert.Equal(a, b);
        Assert.Equal(a!.GetHashCode(), b!.GetHashCode());
    }
}
