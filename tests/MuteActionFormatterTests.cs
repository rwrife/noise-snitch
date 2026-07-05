using NoiseSnitch.Model;
using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Pure wording behind the v0.2 "Mute-the-snitched" action (issue #7): the
/// per-row context-menu label, the balloon feedback for each
/// <see cref="MuteOutcome"/>, and whether a toggle is offered at all. No WinForms
/// and no WASAPI are touched.
/// </summary>
public sealed class MuteActionFormatterTests
{
    [Fact]
    public void ToggleLabel_Mute_When_Not_Currently_Muted()
    {
        // "chrome" resolves to its friendly name; the verb is "Mute" when unmuted.
        Assert.Equal("Mute Google Chrome",
            MuteActionFormatter.ToggleLabel(4821, "chrome", currentlyMuted: false));
    }

    [Fact]
    public void ToggleLabel_Unmute_When_Currently_Muted()
    {
        Assert.Equal("Unmute Google Chrome",
            MuteActionFormatter.ToggleLabel(4821, "chrome", currentlyMuted: true));
    }

    [Fact]
    public void ToggleLabel_Falls_Back_To_Pid_When_Name_Missing()
    {
        Assert.Equal("Mute pid 4821",
            MuteActionFormatter.ToggleLabel(4821, "   ", currentlyMuted: false));
    }

    [Theory]
    [InlineData(MuteOutcome.Muted, "Muted Google Chrome.")]
    [InlineData(MuteOutcome.Unmuted, "Unmuted Google Chrome.")]
    public void Feedback_Reports_New_State(MuteOutcome outcome, string expected)
    {
        Assert.Equal(expected, MuteActionFormatter.Feedback(outcome, 4821, "chrome"));
    }

    [Fact]
    public void Feedback_NoSession_Explains_Nothing_To_Mute()
    {
        string msg = MuteActionFormatter.Feedback(MuteOutcome.NoSession, 4821, "chrome");
        Assert.Contains("Google Chrome", msg);
        Assert.Contains("nothing to mute", msg);
    }

    [Fact]
    public void Feedback_Failed_Suggests_Retry()
    {
        string msg = MuteActionFormatter.Feedback(MuteOutcome.Failed, 4821, "chrome");
        Assert.Contains("Couldn't change", msg);
        Assert.Contains("Google Chrome", msg);
    }

    [Fact]
    public void Feedback_SystemSounds_Is_Declined_Without_A_Name()
    {
        // pid 0 is system sounds; the message shouldn't try to name a process.
        string msg = MuteActionFormatter.Feedback(MuteOutcome.SystemSoundsDeclined, 0, "");
        Assert.Equal("System sounds can't be muted from here.", msg);
    }

    [Fact]
    public void CanOfferToggle_False_For_System_Sounds()
    {
        Assert.False(MuteActionFormatter.CanOfferToggle(0));
    }

    [Fact]
    public void CanOfferToggle_True_For_Real_Process()
    {
        Assert.True(MuteActionFormatter.CanOfferToggle(4821));
    }
}
