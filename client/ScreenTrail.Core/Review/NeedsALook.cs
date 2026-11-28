using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Review;

/// <summary>One sentence of the note that needs a look, where it is, and why, in plain words.</summary>
/// <param name="Where">"Problem", "Step 2", "Result", "Follow-up 1".</param>
/// <param name="Sentence">The sentence as written, less any character that does not show.</param>
/// <param name="Reason">Why it is marked: "tells the reader to do something, has a number to call".</param>
public sealed record LookMark(string Where, string Sentence, string Reason);

/// <summary>
/// "Needs a look" (E-01, DEC-01). Every sentence of the note that tells the reader to do something, or
/// holds an address, a number to call or a command, is marked, so the technician reads it before a
/// customer does. A Discussion note waits for their tick (<see cref="Publish.PublishPanel.NeedsALookChecked"/>).
///
/// It marks rather than refuses. An honest note holds host names, paths and numbers, and a follow-up is
/// often an instruction; a refusal would turn those into failed drafts. A mark costs one tick, and only
/// when the customer will read the note. So it errs towards marking: a word that is both a past tense
/// and a command (set, reset, read) is left alone, because it opens a past-tense step, which is how notes
/// are written.
///
/// The sentence is read as it shows: characters that do not show are removed, fullwidth letters are
/// read as plain ones, and letters borrowed from another alphabet that look like Latin ones are read as
/// the Latin letter, besides being marked for it.
/// </summary>
public static partial class NeedsALook
{
    public const string Instruction = "tells the reader to do something";
    public const string Address = "has a web, email or network address";
    public const string NumberToCall = "has a number to call";
    public const string Command = "has a command";
    public const string MixedLetters = "mixes letters from different alphabets";
    public const string AnotherLanguage = "is in another language";

    /// <summary>Verbs that open an instruction. Base forms only; a past tense opens what was done.</summary>
    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    {
        "open", "visit", "go", "navigate", "browse", "click", "tap", "press", "type", "enter", "paste", "copy",
        "download", "upload", "install", "uninstall", "reinstall", "run", "execute", "launch", "start", "stop",
        "restart", "reboot", "disable", "enable", "turn", "switch", "remove", "delete", "erase", "wipe", "add",
        "allow", "exclude", "whitelist", "grant", "give", "share", "send", "email", "mail", "message", "text",
        "call", "phone", "ring", "dial", "contact", "reply", "respond", "forward", "provide", "confirm", "verify",
        "sign", "log", "login", "logon", "scan", "fetch", "get", "grab", "map", "connect", "disconnect", "follow",
        "tell", "use", "change", "update", "upgrade", "buy", "purchase", "pay", "transfer", "wire", "approve",
        "accept", "ignore", "check", "make", "try", "keep", "leave", "do", "don't", "dont", "never", "always",
        "avoid", "ensure", "select", "choose", "pick", "write", "print", "save", "move", "rename", "schedule",
        "monitor", "replace", "watch", "unplug", "plug", "power", "bring", "take", "look", "see", "note",
        "remember", "report", "ask", "return", "submit", "complete", "finish", "fill", "register", "activate",
        "deactivate", "authorize", "authorise", "trust", "unblock", "block", "close", "clear", "empty",
        "configure", "apply", "consent", "review", "test", "find", "search",
    };

    /// <summary>Words that may come before the verb of an instruction without changing what it is.</summary>
    private static readonly HashSet<string> Openers = new(StringComparer.Ordinal)
    {
        "then", "and", "now", "next", "also", "first", "second", "finally", "just", "simply", "so", "please",
        "kindly", "quickly", "immediately", "afterwards", "lastly",
    };

    /// <summary>Endings that make a dotted word a file name rather than a host.</summary>
    private static readonly HashSet<string> FileEndings = new(StringComparer.Ordinal)
    {
        "exe", "msi", "msix", "appx", "dll", "sys", "ps1", "psm1", "bat", "cmd", "vbs", "js", "jse", "wsf", "hta",
        "scr", "lnk", "txt", "log", "csv", "json", "xml", "ini", "cfg", "conf", "config", "yaml", "yml", "md",
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "rtf", "odt", "png", "jpg", "jpeg", "gif", "bmp",
        "tif", "tiff", "svg", "ico", "rar", "7z", "cab", "iso", "img", "vhd", "vhdx", "bak", "tmp", "dat", "db",
        "sqlite", "mdb", "accdb", "pst", "ost", "eml", "msg", "html", "htm", "css", "php", "py", "rb", "sh",
        "reg", "inf", "cat", "evtx", "etl", "dmp", "mp3", "mp4", "wav", "avi", "mov", "mkv", "old", "new",
    };

    /// <summary>Common short words of English, to tell an English sentence from another language's.</summary>
    private static readonly HashSet<string> English = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "to", "of", "in", "on", "at", "for", "with", "was", "were", "is", "are",
        "it", "its", "from", "by", "then", "after", "before", "not", "no", "be", "been", "has", "had", "have",
        "this", "that", "these", "those", "there", "their", "they", "he", "she", "we", "you", "i", "my", "our",
        "your", "as", "but", "if", "so", "into", "over", "under", "up", "out", "about", "than", "when", "while",
        "all", "any", "each", "also", "only", "very", "just", "more", "most", "again", "now",
    };

    /// <summary>Common short words of the languages most often met beside English, none of them English words.</summary>
    private static readonly HashSet<string> Foreign = new(StringComparer.Ordinal)
    {
        // German
        "sie", "und", "der", "das", "den", "dem", "des", "ist", "nicht", "bitte", "ein", "eine", "einen", "mit",
        "auf", "für", "ich", "wir", "ihr", "ihre", "ihren", "auch", "oder", "wenn", "dass", "zu", "neu",
        // French
        "vous", "votre", "vos", "veuillez", "les", "une", "avec", "pour", "dans", "est", "pas", "sur", "qui",
        "que", "cliquez", "ouvrez", "et", "le", "la",
        // Spanish and Portuguese
        "usted", "ustedes", "favor", "los", "las", "una", "con", "para", "del", "haga", "está", "você", "voce",
        "uma", "com", "não", "nao", "por",
        // Italian and Dutch
        "il", "gli", "della", "sono", "het", "een", "niet", "uw", "alstublieft", "wilt",
    };

    /// <summary>Letters of other alphabets that are drawn like a Latin letter, and the letter they are drawn like.</summary>
    private static readonly Dictionary<char, char> LookAlikes = new()
    {
        // Cyrillic
        ['\u0430'] = 'a',
        ['\u0435'] = 'e',
        ['\u043E'] = 'o',
        ['\u0440'] = 'p',
        ['\u0441'] = 'c',
        ['\u0443'] = 'y',
        ['\u0445'] = 'x',
        ['\u0456'] = 'i',
        ['\u0458'] = 'j',
        ['\u0455'] = 's',
        ['\u0501'] = 'd',
        ['\u04BB'] = 'h',
        ['\u0410'] = 'A',
        ['\u0412'] = 'B',
        ['\u0415'] = 'E',
        ['\u041A'] = 'K',
        ['\u041C'] = 'M',
        ['\u041D'] = 'H',
        ['\u041E'] = 'O',
        ['\u0420'] = 'P',
        ['\u0421'] = 'C',
        ['\u0422'] = 'T',
        ['\u0425'] = 'X',
        ['\u0406'] = 'I',
        ['\u0408'] = 'J',
        ['\u0405'] = 'S',
        // Greek
        ['\u03BF'] = 'o',
        ['\u03B1'] = 'a',
        ['\u03BD'] = 'v',
        ['\u03C1'] = 'p',
        ['\u03B9'] = 'i',
        ['\u03BA'] = 'k',
        ['\u0391'] = 'A',
        ['\u0392'] = 'B',
        ['\u0395'] = 'E',
        ['\u0396'] = 'Z',
        ['\u0397'] = 'H',
        ['\u0399'] = 'I',
        ['\u039A'] = 'K',
        ['\u039C'] = 'M',
        ['\u039D'] = 'N',
        ['\u039F'] = 'O',
        ['\u03A1'] = 'P',
        ['\u03A4'] = 'T',
        ['\u03A5'] = 'Y',
        ['\u03A7'] = 'X',
    };

    /// <summary>Every sentence of the note that needs a look, in the note's order. Empty for none, or no note.</summary>
    public static IReadOnlyList<LookMark> Find(DraftNote? note)
    {
        if (note is null)
        {
            return [];
        }

        var marks = new List<LookMark>();
        Add(marks, "Problem", note.Problem);
        for (var i = 0; i < note.Steps.Count; i++)
        {
            Add(marks, string.Create(CultureInfo.InvariantCulture, $"Step {i + 1}"), note.Steps[i].Text);
        }

        Add(marks, "Result", note.Result);
        for (var i = 0; i < note.FollowUps.Count; i++)
        {
            Add(marks, string.Create(CultureInfo.InvariantCulture, $"Follow-up {i + 1}"), note.FollowUps[i]);
        }

        return marks;
    }

    /// <summary>Why one sentence needs a look, in plain words. Empty when it does not.</summary>
    public static IReadOnlyList<string> Reasons(string sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        var shown = Unhidden(sentence);
        var mixed = Mixes(shown);
        var read = Read(shown);
        var lower = read.ToLowerInvariant();
        var reasons = new List<string>();

        if (IsInstruction(lower))
        {
            reasons.Add(Instruction);
        }

        if (HasAddress(lower))
        {
            reasons.Add(Address);
        }

        if (HasNumberToCall(lower))
        {
            reasons.Add(NumberToCall);
        }

        if (HasCommand(lower))
        {
            reasons.Add(Command);
        }

        if (mixed)
        {
            reasons.Add(MixedLetters);
        }

        if (IsAnotherLanguage(shown, lower))
        {
            reasons.Add(AnotherLanguage);
        }

        return reasons;
    }

    /// <summary>The sentences of one field, by full stops, question and exclamation marks, and line breaks.</summary>
    public static IEnumerable<string> Sentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var line in Unhidden(text).Split('\n'))
        {
            foreach (var sentence in SentenceEnd().Split(line))
            {
                if (sentence.Trim() is { Length: > 0 } trimmed)
                {
                    yield return trimmed;
                }
            }
        }
    }

    private static void Add(List<LookMark> marks, string where, string? text)
    {
        foreach (var sentence in Sentences(text))
        {
            if (Reasons(sentence) is { Count: > 0 } reasons)
            {
                marks.Add(new LookMark(where, sentence, string.Join(", ", reasons)));
            }
        }
    }

    /// <summary>The text without its format characters: zero-width ones, the bidirectional controls, soft hyphens.</summary>
    private static string Unhidden(string text)
    {
        var kept = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
            {
                _ = kept.Append(rune.ToString());
            }
        }

        return kept.ToString();
    }

    /// <summary>Fullwidth forms as the plain letters they are, look-alike letters as Latin, every space a plain one.</summary>
    private static string Read(string text)
    {
        var read = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            _ = read.Append(c switch
            {
                >= '\uFF01' and <= '\uFF5E' => (char)(c - 0xFEE0),
                _ when LookAlikes.TryGetValue(c, out var latin) => latin,
                _ when char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) is UnicodeCategory.SpaceSeparator => ' ',
                _ => c,
            });
        }

        return read.ToString();
    }

    private static bool IsInstruction(string lower)
    {
        if (Polite().IsMatch(lower))
        {
            return true;
        }

        // Each clause: "Reboot, then run this" is two instructions, and the second is the one that matters.
        foreach (var clause in Clause().Split(lower))
        {
            var words = Word().Matches(Leading().Replace(clause, string.Empty)).Select(m => m.Value).ToList();
            var at = 0;
            while (at < words.Count - 1 && at < 2 && Openers.Contains(words[at]))
            {
                at++;
            }

            if (at < words.Count && Verbs.Contains(words[at]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAddress(string lower) =>
        Scheme().IsMatch(lower)
        || lower.Contains("](", StringComparison.Ordinal)
        || ProtocolRelative().IsMatch(lower)
        || Www().IsMatch(lower)
        || Defanged().IsMatch(lower)
        || EmailAddress().IsMatch(lower)
        || Unc().IsMatch(lower)
        || IpAddress().IsMatch(lower)
        || Domain().Matches(lower).Any(m => !FileEndings.Contains(m.Groups["end"].Value));

    private static bool HasNumberToCall(string lower) => Phone().Matches(lower).Any(m =>
    {
        var digits = m.Value.Count(char.IsAsciiDigit);
        return digits is >= 7 and <= 15 && !DateLike().IsMatch(m.Value.Trim());
    });

    private static bool HasCommand(string lower) =>
        Program().IsMatch(lower) || WithSubcommand().IsMatch(lower) || Cmdlet().IsMatch(lower);

    /// <summary>A word holding Latin letters and letters of another alphabet: "Dіsable" with a Cyrillic і.</summary>
    private static bool Mixes(string text)
    {
        foreach (Match word in Letters().Matches(text))
        {
            var latin = false;
            var other = false;
            foreach (var c in word.Value)
            {
                if (IsLatin(c))
                {
                    latin = true;
                }
                else if (char.IsLetter(c))
                {
                    other = true;
                }
            }

            if (latin && other)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Another language: mostly letters of another alphabet, or more of the short words of a language met
    /// beside English than of English's own, and at least three of them.
    /// </summary>
    private static bool IsAnotherLanguage(string shown, string lower)
    {
        var latinLetters = shown.Count(IsLatin);
        var otherLetters = shown.Count(c => char.IsLetter(c) && !IsLatin(c));
        if (otherLetters >= 6 && otherLetters >= latinLetters)
        {
            return true;
        }

        var words = Word().Matches(lower).Select(m => m.Value).ToList();
        var foreign = words.Count(Foreign.Contains);
        var english = words.Count(English.Contains);
        return foreign >= 3 && foreign > english;
    }

    private static bool IsLatin(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '\u00C0' and <= '\u024F' and not '\u00D7' and not '\u00F7') or (>= '\u1E00' and <= '\u1EFF');

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\bplease\b|\byou\s+(?:should|must|need|have\s+to|will\s+need|can\s+now)\b|\bmake\s+sure\b|\bbe\s+sure\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Polite();

    [GeneratedRegex(@"[,;:]\s+then\b|;\s+|\s+then\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Clause();

    /// <summary>A bullet, a step number, or an opening bracket or quote before the first word.</summary>
    [GeneratedRegex(@"\A[\s\-*\u2022\[\(""'\u201C\u2018]*(?:\d+[.)]\s*)?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Leading();

    [GeneratedRegex(@"[\p{L}']+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Word();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Letters();

    [GeneratedRegex(@"\b[a-z][a-z0-9+.\-]{1,15}://|\b(?:mailto|tel|callto|file|javascript|data|ms-[a-z\-]+):", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Scheme();

    [GeneratedRegex(@"(?<![\w:/])//[a-z0-9\-]+[./]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ProtocolRelative();

    [GeneratedRegex(@"\bwww\d?\.", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Www();

    [GeneratedRegex(@"\[\.\]|\(\.\)|\{\.\}|\[dot\]|\(dot\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Defanged();

    [GeneratedRegex(@"[a-z0-9._%+\-]+@[a-z0-9\-]+(?:\.[a-z0-9\-]+)*\.[a-z]{2,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\\\\[^\s\\]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Unc();

    [GeneratedRegex(@"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.]*\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IpAddress();

    /// <summary>A dotted name whose last part is letters: portal.example, files.acme.example/share.</summary>
    [GeneratedRegex(@"\b[a-z0-9](?:[a-z0-9\-]{0,62}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9\-]{0,62}[a-z0-9])?)*\.(?<end>[a-z]{2,24})\b(?!\.\w|-)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Domain();

    /// <summary>Digits with the separators a phone number is written with, standing alone.</summary>
    [GeneratedRegex(@"(?<![\w.:/\-])\+?[\d(][\d\s().\-]{5,}\d(?![\w/:\-])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Phone();

    [GeneratedRegex(@"\A(?:\d{4}-\d{1,2}-\d{1,2}|\d{1,2}[.\-]\d{1,2}[.\-]\d{2,4})\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex DateLike();

    [GeneratedRegex(@"\b(?:powershell|pwsh|cmd|msiexec|bcdedit|regedit|netsh|wmic|schtasks|certutil|bitsadmin|mshta|rundll32|regsvr32|cscript|wscript|vssadmin|wevtutil|diskpart|takeown|icacls|cacls|manage-bde|iex|iwr|irm|curl|wget|sudo|chmod|robocopy|xcopy|taskkill|winget|choco|rmdir)(?:\.exe)?\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Program();

    [GeneratedRegex(@"\bnet1?\s+(?:user|localgroup|use|stop|start|share|group|accounts|session)\b|\bsc(?:\.exe)?\s+(?:stop|start|delete|config|create|query)\b|\breg(?:\.exe)?\s+(?:add|delete|import|load|save|export|query)\b|\bdel\s+(?:/\w+\s+)*[a-z]:|\brd\s+/s\b|\bformat\s+[a-z]:|\bshutdown\s+/", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex WithSubcommand();

    /// <summary>A PowerShell command: a verb, a hyphen and a noun, as in Add-MpPreference. Not add-on or set-up.</summary>
    [GeneratedRegex(@"\b(?:get|set|add|remove|new|invoke|start|stop|restart|enable|disable|import|export|install|uninstall|clear|copy|move|rename|test|update|register|unregister|grant|revoke)-(?!(?:up|on|in|out|ins|ons|ups|off)\b)[a-z]{2,}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Cmdlet();
}
