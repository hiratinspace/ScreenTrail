using ScreenTrail.Core.Privacy;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// A-05 (a): what the focused-field probe's window-message fallback may conclude. The message asks an edit
/// control for its password character; a window that is not one passes it to its default procedure, which
/// answers 0 whatever the window shows. So a 0 means "not a password field" only from an edit control, and
/// from anything else (a browser's render widget, a WPF, WinUI, Electron or Java window, a remote viewer's
/// surface) it is no answer, and the guard keeps its last decision instead of lifting a hold on it.
/// </summary>
public sealed class PasswordCharacterAnswerTests
{
    [Theory]
    [InlineData("Edit")]
    [InlineData("EDIT")]
    [InlineData("RichEdit20W")]
    [InlineData("RICHEDIT50W")]
    [InlineData("RichEditD2DPT")]
    [InlineData("TEdit")]
    [InlineData("TMaskEdit")]
    [InlineData("ThunderRT6TextBox")]
    [InlineData("WindowsForms10.EDIT.app.0.141b42a_r8_ad1")]
    public void AnEditControlThatHasNoPasswordCharacterIsNotAPasswordField(string windowClass)
    {
        Assert.Equal(FocusedField.NotPassword, PasswordCharacterAnswer.From(windowClass, 0));
        Assert.Equal(FocusedField.Password, PasswordCharacterAnswer.From(windowClass, '*'));
    }

    [Theory]
    [InlineData("Chrome_RenderWidgetHostHWND")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("MozillaWindowClass")]
    [InlineData("HwndWrapper[App;;8f2c1d0e-3b4a-4c5d-9e6f-0a1b2c3d4e5f]")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("SunAwtFrame")]
    [InlineData("TscShellContainerClass")]
    [InlineData("IHWindowClass")]
    [InlineData("STATIC")]
    [InlineData("Button")]
    [InlineData("EditPlus")]
    [InlineData("")]
    [InlineData(null)]
    public void ZeroFromAWindowThatIsNotAnEditControlIsNoAnswer(string? windowClass) =>
        Assert.Equal(FocusedField.Unknown, PasswordCharacterAnswer.From(windowClass, 0));

    /// <summary>A password character is a password field whatever the class: the error that suppresses, not the one that records.</summary>
    [Theory]
    [InlineData("Chrome_RenderWidgetHostHWND")]
    [InlineData(null)]
    public void APasswordCharacterFromAnyWindowIsAPasswordField(string? windowClass) =>
        Assert.Equal(FocusedField.Password, PasswordCharacterAnswer.From(windowClass, '●'));
}
