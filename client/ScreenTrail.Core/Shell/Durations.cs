using System.Globalization;

namespace ScreenTrail.Core.Shell;

/// <summary>
/// How long something took, in one format wherever a technician reads it (review finding E-08). All
/// sessions said "21:00" and "1:02:00", the rail "21 min", and the receipt's time entry "0:15" under an
/// envelope that had said "15 min", which read as fifteen seconds. Clock times (14:02) are not durations
/// and keep their own format.
/// </summary>
public static class Durations
{
    /// <summary>"8 s" under a minute, "21 min" under an hour, then "1 h 2 min" (or "2 h" on the hour).</summary>
    public static string InWords(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        var seconds = (long)Math.Round(span.TotalSeconds, MidpointRounding.AwayFromZero);
        return seconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds} s")
            : OfMinutes((int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));
    }

    /// <inheritdoc cref="InWords(TimeSpan)"/>
    public static string InWords(long milliseconds) => InWords(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)));

    /// <summary>A whole number of minutes, as a time entry is: "15 min", "1 h 30 min", "2 h".</summary>
    public static string OfMinutes(int minutes)
    {
        minutes = Math.Max(0, minutes);
        return minutes < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes} min")
            : minutes % 60 == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h")
                : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60} min");
    }
}
