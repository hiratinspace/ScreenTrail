using System.Globalization;
using System.Text;

namespace ScreenTrail.Core.Detection;

/// <summary>
/// A window class or process name made safe to put on one log line.
///
/// Another application chooses its window class, and AnyDesk's ends in a line break ("ad_win#2\n", seen
/// on the laptop on 2026-11-22). Logged as it came, the rest of the line landed on a line of its own,
/// which the demo report then withheld as not written by ScreenTrail's logger. Control characters are
/// written as escapes instead, so a log line stays one line whatever an application names its windows.
/// </summary>
public static class PrintableText
{
    public static string Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var clean = true;
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                clean = false;
                break;
            }
        }

        if (clean)
        {
            return text;
        }

        var shown = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            _ = c switch
            {
                '\n' => shown.Append("\\n"),
                '\r' => shown.Append("\\r"),
                '\t' => shown.Append("\\t"),
                _ when char.IsControl(c) => shown.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture)),
                _ => shown.Append(c),
            };
        }

        return shown.ToString();
    }
}
