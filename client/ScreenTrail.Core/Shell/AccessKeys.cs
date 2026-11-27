namespace ScreenTrail.Core.Shell;

/// <summary>
/// Access keys for labels worded at run time, such as the dialog's buttons (review finding E-20): each gets
/// one, unique within the window, so Alt+K presses "Keep it" as Windows users expect. WPF reads the letter
/// after an underscore as the key and draws it underlined while Alt is held.
/// </summary>
public static class AccessKeys
{
    /// <summary>
    /// Each label with an underscore before its key: the first letter of its first word not taken by an
    /// earlier label, then of its later words, then any letter or digit in it. Keys are unique without
    /// regard to case and never one of <paramref name="reserved"/>. A label with no letter left keeps none.
    /// An underscore already in a label is doubled, so it is shown rather than read as a key.
    /// </summary>
    public static IReadOnlyList<string> Assign(IReadOnlyList<string> labels, string reserved = "")
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(reserved);
        var taken = new HashSet<char>(reserved.Select(char.ToUpperInvariant));
        var keyed = new List<string>(labels.Count);
        foreach (var label in labels)
        {
            ArgumentNullException.ThrowIfNull(label);
            var at = Candidates(label).Where(i => !taken.Contains(char.ToUpperInvariant(label[i]))).DefaultIfEmpty(-1).First();
            var escaped = label.Replace("_", "__", StringComparison.Ordinal);
            if (at < 0)
            {
                keyed.Add(escaped);
                continue;
            }

            _ = taken.Add(char.ToUpperInvariant(label[at]));
            var shift = label[..at].Count(c => c == '_');
            keyed.Add(escaped.Insert(at + shift, "_"));
        }

        return keyed;
    }

    /// <summary>The key a keyed label offers, upper case, or null for none.</summary>
    public static char? KeyOf(string keyed)
    {
        ArgumentNullException.ThrowIfNull(keyed);
        for (var i = 0; i < keyed.Length - 1; i++)
        {
            if (keyed[i] != '_')
            {
                continue;
            }

            if (keyed[i + 1] == '_')
            {
                i++;
                continue;
            }

            return char.ToUpperInvariant(keyed[i + 1]);
        }

        return null;
    }

    private static IEnumerable<int> Candidates(string label)
    {
        for (var i = 0; i < label.Length; i++)
        {
            if (char.IsLetterOrDigit(label[i]) && (i == 0 || label[i - 1] == ' '))
            {
                yield return i;
            }
        }

        for (var i = 0; i < label.Length; i++)
        {
            if (char.IsLetterOrDigit(label[i]))
            {
                yield return i;
            }
        }
    }
}
