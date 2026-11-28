namespace ScreenTrail.Core.Privacy;

/// <summary>
/// What the focused-field probe's window-message fallback may conclude from <c>EM_GETPASSWORDCHAR</c>
/// (ST-040, A-05). Only an edit control handles the message; any other window passes it to its default
/// procedure, which answers 0 whatever the window shows. So a password character is a password field from
/// any window, and a 0 is "not a password field" only from an edit control. From anything else (a
/// browser's render widget, a WPF, WinUI, Electron or Java window, a remote viewer's surface) it is no
/// answer, and <see cref="PasswordFieldGuard"/> keeps its last decision rather than lifting a hold on it.
/// Kept apart from the Windows probe so the rule is tested on every machine.
/// </summary>
public static class PasswordCharacterAnswer
{
    /// <summary>
    /// Window classes that are an edit control, or a toolkit's text box built on one and so answering the
    /// message: the system's Edit and RichEdit (every version), Windows Forms' (its class names carry the
    /// system class: <c>WindowsForms10.EDIT.app...</c>), Delphi's VCL and Visual Basic 6's.
    /// </summary>
    private static readonly HashSet<string> EditClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit",
        "TEdit",
        "TMaskEdit",
        "TLabeledEdit",
        "TButtonedEdit",
        "TDBEdit",
        "TSpinEdit",
        "ThunderTextBox",
        "ThunderRT6TextBox",
    };

    /// <param name="windowClass">The focused window's class name, or null if it could not be read.</param>
    /// <param name="passwordCharacter">What the window answered to <c>EM_GETPASSWORDCHAR</c>.</param>
    public static FocusedField From(string? windowClass, long passwordCharacter) =>
        passwordCharacter != 0 ? FocusedField.Password
        : IsEditControl(windowClass) ? FocusedField.NotPassword
        : FocusedField.Unknown;

    private static bool IsEditControl(string? windowClass) =>
        windowClass is { Length: > 0 } name
        && (EditClasses.Contains(name)
            || name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("WindowsForms10.RichEdit", StringComparison.OrdinalIgnoreCase));
}
