using ScreenTrail.Core.Detection;

namespace ScreenTrail.Tests.Detection;

public sealed class PrintableTextTests
{
    [Theory]
    [InlineData("ad_win#2\n", "ad_win#2\\n")]
    [InlineData("a\r\nb", "a\\r\\nb")]
    [InlineData("tab\there", "tab\\there")]
    [InlineData("bell\u0007", "bell\\x07")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", "CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("HwndWrapper[ScreenTrail.UI;;910f]", "HwndWrapper[ScreenTrail.UI;;910f]")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ControlCharactersAreEscapedAndTheRestIsKept(string? raw, string expected) =>
        Assert.Equal(expected, PrintableText.Of(raw));

    [Fact]
    public void AnEscapedNameIsAlwaysOneLine() =>
        Assert.DoesNotContain('\n', PrintableText.Of("one\ntwo\nthree"));
}
