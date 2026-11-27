using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ScreenTrail.Core.Notifications;

/// <summary>
/// A notice as a Windows toast (review findings C-T6 and D-10, design system 10.6), where it used to be a
/// tray balloon that Windows turned into a generic toast: "ScreenTrail" as the header and again as the
/// title, no button, and an entry that opened nothing.
///
/// The title is the event ("Draft ready to check"), the body the notice's words, and a notice with
/// something to press has one button that says what it opens. Windows draws the header from the app's
/// identity (<see cref="AppId"/>, on the Start menu shortcut), so it reads "ScreenTrail" with the app icon
/// once. Focus Assist is still Windows' to apply, and the rule that nothing is said during a session is
/// <see cref="Notices"/>'s, unchanged.
/// </summary>
public static class ToastContent
{
    /// <summary>
    /// The app's identity to Windows (AppUserModelID): the Start menu shortcut carries it, and the UI
    /// process says it at start, so a toast is ScreenTrail's and the notification settings list it by name.
    /// </summary>
    public const string AppId = "ScreenTrail.Client";

    /// <summary>The group every ScreenTrail toast is in, so a notice can replace the last of its kind.</summary>
    public const string Group = "screentrail";

    /// <summary>What a toast's launch and its button say, read back when it is pressed.</summary>
    public const string ArgumentName = "action";

    /// <summary>The longest tag Windows accepts.</summary>
    public const int MaxTag = 64;

    /// <summary>The toast's XML (ToastGeneric). Every word is escaped by the XML writer, not by hand.</summary>
    public static string Xml(Notification notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var argument = $"{ArgumentName}={notice.Action}";
        var toast = new XElement(
            "toast",
            new XAttribute("launch", argument),
            new XElement(
                "visual",
                new XElement(
                    "binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", notice.Title),
                    new XElement("text", notice.Text))));
        if (ButtonFor(notice.Action) is { } button)
        {
            toast.Add(new XElement(
                "actions",
                new XElement(
                    "action",
                    new XAttribute("content", button),
                    new XAttribute("arguments", argument),
                    new XAttribute("activationType", "foreground"))));
        }

        return toast.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>The one button, named for what it opens; none for a notice with nothing to press.</summary>
    public static string? ButtonFor(NotificationAction action) => action switch
    {
        NotificationAction.Review => "Open draft",
        NotificationAction.Open => "Open in ScreenTrail",
        _ => null,
    };

    /// <summary>
    /// The toast's tag: the notice's <see cref="Notification.Key"/>, so the same notice replaces itself
    /// rather than stacking, and at most <see cref="MaxTag"/> characters; a longer key is hashed.
    /// </summary>
    public static string Tag(Notification notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return notice.Key.Length <= MaxTag
            ? notice.Key
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notice.Key)))[..MaxTag];
    }

    /// <summary>The action a pressed toast's arguments name, or None.</summary>
    public static NotificationAction ActionOf(string? arguments)
    {
        const string prefix = ArgumentName + "=";
        return arguments is not null
            && arguments.StartsWith(prefix, StringComparison.Ordinal)
            && Enum.TryParse<NotificationAction>(arguments[prefix.Length..], out var action)
            && Enum.IsDefined(action)
            ? action
            : NotificationAction.None;
    }

    /// <summary>How long a toast stays in the notification centre: a day, after which it is stale.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
}
