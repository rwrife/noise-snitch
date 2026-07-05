using NoiseSnitch.Ui;
using Xunit;

namespace NoiseSnitch.Tests;

/// <summary>
/// Pure gate/normalization logic behind the M5 blotter icon cache. No
/// <c>System.Drawing</c> and no live process/exe is touched, so these run on any
/// OS (including the Linux/CI test host).
/// </summary>
public sealed class IconKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsResolvable_False_For_Blank(string? path)
    {
        Assert.False(IconKey.IsResolvable(path));
        Assert.Equal(string.Empty, IconKey.Normalize(path));
    }

    [Theory]
    [InlineData("chrome")]            // bare name, not a path
    [InlineData("chrome.exe")]        // still just a name
    [InlineData("pid:1234")]          // enumerator placeholder
    [InlineData("pid:1234 (exited)")] // exited placeholder
    [InlineData("System Sounds")]     // pseudo-session label
    public void IsResolvable_False_For_Names_And_Placeholders(string path)
    {
        Assert.False(IconKey.IsResolvable(path));
        Assert.Equal(string.Empty, IconKey.Normalize(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"D:\apps\thing\thing.exe")]
    [InlineData(@"\\server\share\app\app.exe")] // UNC
    public void IsResolvable_True_For_Rooted_Paths(string path)
    {
        Assert.True(IconKey.IsResolvable(path));
        Assert.NotEqual(string.Empty, IconKey.Normalize(path));
    }

    [Fact]
    public void Normalize_Is_Case_Insensitive_And_Separator_Agnostic()
    {
        // Same file, three spellings -> one cache key.
        string a = IconKey.Normalize(@"C:\Apps\Foo\Foo.exe");
        string b = IconKey.Normalize(@"c:\apps\foo\foo.exe");
        string c = IconKey.Normalize("C:/Apps/Foo/Foo.exe");

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Normalize_Trims_Whitespace_And_Trailing_Separator()
    {
        string key = IconKey.Normalize(@"  C:\Apps\Foo\  ");
        Assert.Equal(@"c:\apps\foo", key);
    }

    [Fact]
    public void Normalize_Keeps_Drive_Root_Separator()
    {
        // Must not strip the separator that makes "C:\" a rooted path.
        Assert.Equal(@"c:\", IconKey.Normalize(@"C:\"));
    }
}
