using ScreenTrail.Core.Shell;

namespace ScreenTrail.Tests.Shell;

/// <summary>Access keys for labels worded at run time (review finding E-20).</summary>
public sealed class AccessKeysTests
{
    [Fact]
    public void TheDialogsTakeTheKeysWindowsUsersExpect()
    {
        Assert.Equal(["_Keep it", "_Discard session"], AccessKeys.Assign(["Keep it", "Discard session"]));
        Assert.Equal(["_Keep capturing", "_Stop and quit"], AccessKeys.Assign(["Keep capturing", "Stop and quit"]));
        Assert.Equal(["_Close"], AccessKeys.Assign(["Close"]));
    }

    [Fact]
    public void ATakenLetterMovesToTheNextWordThenAnyLetter()
    {
        Assert.Equal(["_Keep everything", "Keep _going"], AccessKeys.Assign(["Keep everything", "Keep going"]));
        Assert.Equal(["_Keep", "K_eep"], AccessKeys.Assign(["Keep", "Keep"]));
        Assert.Equal(["C_lose"], AccessKeys.Assign(["Close"], reserved: "c"));
    }

    [Fact]
    public void ALabelWithNoLetterLeftKeepsNoneAndUnderscoresAreShown()
    {
        Assert.Equal(["_A", "A"], AccessKeys.Assign(["A", "A"]));
        Assert.Equal("my__file _now", AccessKeys.Assign(["my_file now"], reserved: "mfyile")[0]);
        Assert.Equal('N', AccessKeys.KeyOf("my__file _now"));
        Assert.Null(AccessKeys.KeyOf("my__file"));
    }
}
