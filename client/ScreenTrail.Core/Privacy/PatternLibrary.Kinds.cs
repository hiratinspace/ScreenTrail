using System.Text.RegularExpressions;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Privacy;

/// <summary>
/// The kinds of secret an MSP's sessions show that have a shape of their own (K2, DEC-08): credentials on
/// a command line and in a web address, a Wi-Fi key as Windows lists it, recovery and MFA setup keys, the
/// ITIN, and the password word in every language the sign-in heuristic knows. Each takes only the value,
/// never the command or the label, so the line still says what was done. Every pattern here is bounded and
/// runs under the same match budget as the rest of the library: input built to make one backtrack costs
/// time, never the scan (a timeout is reported incomplete and the frame is discarded, as for any rule).
/// </summary>
internal static partial class PatternLibrary
{
    /// <summary>
    /// The sign-in heuristic's password words that <see cref="PasswordCue"/> does not already match, as one
    /// cue: "Kennwort:", "Mot de passe :", "contraseña", "passwort ist ..." (K2: one cue list for both). Built
    /// from <see cref="LoginScreenHeuristic.PasswordWords"/>, so a word added there is masked here too.
    /// </summary>
    private static readonly Regex SharedPasswordCue = new(
        @"(?<![\p{L}\p{N}])(?:"
        + string.Join('|', LoginScreenHeuristic.PasswordWords
            .Where(word => !PasswordCue().IsMatch(word))
            .Select(word => Regex.Escape(word).Replace(@"\ ", @"\s+", StringComparison.Ordinal)))
        + @")(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(BuiltInTimeoutMs));

    /// <summary>The credentials that follow a cue of their own and a value the cue rule's walk reads.</summary>
    private static IEnumerable<PatternMatch> SharedCuePasswords(string text, Action onIncomplete)
    {
        foreach (Match m in Safely(SharedPasswordCue.Matches(text), onIncomplete))
        {
            if (SecretAfter(text, m.Index + m.Length) is { Length: > 0 } secret)
            {
                yield return new PatternMatch(MaskKind.Password, secret.Start, secret.Length, "[REDACTED]") { CueStarts = [m.Index] };
            }
        }
    }

    /// <summary>
    /// A password on a command line or in a web address: the value only, which is one token. Command-line
    /// values must look like a secret (<see cref="LooksLikeSecret"/>), because the page's next line is
    /// joined onto a command that ends where its value would be ("net user jdoe" over "User name jdoe").
    /// </summary>
    private static IEnumerable<PatternMatch> CommandLinePasswords(string text, Action onIncomplete)
    {
        foreach (var (index, length) in Values(UrlUserInfo().Matches(text), onIncomplete))
        {
            yield return new PatternMatch(MaskKind.Password, index, length, "[REDACTED]");
        }

        foreach (var regex in new[] { NetUseAfterPath(), NetUseAfterUser(), NetUser(), PsExecPassword() })
        {
            foreach (var (index, length) in Values(regex.Matches(text), onIncomplete))
            {
                if (LooksLikeSecret(text.Substring(index, length)))
                {
                    yield return new PatternMatch(MaskKind.Password, index, length, "[REDACTED]");
                }
            }
        }

        // Quoted, so the quotes say where the value is, whatever it looks like.
        foreach (var (index, length) in Values(SecureStringValue().Matches(text), onIncomplete))
        {
            yield return new PatternMatch(MaskKind.Password, index, length, "[REDACTED]");
        }
    }

    /// <summary>BitLocker recovery keys, MFA setup keys and the secret of an otpauth address.</summary>
    private static IEnumerable<PatternMatch> RecoveryAndSetupKeys(string text, Action onIncomplete)
    {
        foreach (var (index, length) in Values(RecoveryKey().Matches(text), onIncomplete))
        {
            yield return new PatternMatch(MaskKind.ApiKey, index, length, "[SECRET]");
        }

        foreach (var (index, length) in Values(MfaSetupKey().Matches(text), onIncomplete))
        {
            if (LooksLikeSetupKey(text.AsSpan(index, length)))
            {
                yield return new PatternMatch(MaskKind.ApiKey, index, length, "[SECRET]");
            }
        }

        foreach (var (index, length) in Values(OtpAuthSecret().Matches(text), onIncomplete))
        {
            yield return new PatternMatch(MaskKind.ApiKey, index, length, "[SECRET]");
        }
    }

    /// <summary>The ITIN: a social security number's shape in the range that rule leaves out on purpose.</summary>
    private static IEnumerable<PatternMatch> Itins(string text, Action onIncomplete)
    {
        foreach (Match m in Safely(Itin().Matches(text), onIncomplete))
        {
            yield return new PatternMatch(MaskKind.Ssn, m.Index, m.Length, "[SSN]");
        }
    }

    /// <summary>
    /// The United States ids masked only beside their label (<see cref="RedactionPolicy.LabelledIds"/>). A
    /// routing number is masked only when its checksum holds. Counted with the social security numbers:
    /// the counts' kinds are the session schema's, and these are personal ids too.
    /// </summary>
    private static IEnumerable<PatternMatch> LabelledIds(string text, Action onIncomplete)
    {
        foreach (var regex in new[] { MedicareNumber(), PassportNumber(), DriversLicence(), BankAccount() })
        {
            foreach (var (index, length) in Values(regex.Matches(text), onIncomplete))
            {
                yield return new PatternMatch(MaskKind.Ssn, index, length, "[REDACTED]");
            }
        }

        foreach (var (index, length) in Values(RoutingNumber().Matches(text), onIncomplete))
        {
            if (PassesAbaChecksum(text.AsSpan(index, length)))
            {
                yield return new PatternMatch(MaskKind.Ssn, index, length, "[REDACTED]");
            }
        }
    }

    /// <summary>Each match's <c>v</c> group: the value, without the command or the label around it.</summary>
    private static IEnumerable<(int Index, int Length)> Values(MatchCollection matches, Action onIncomplete)
    {
        foreach (Match m in Safely(matches, onIncomplete))
        {
            var value = m.Groups["v"];
            if (value.Success && value.Length > 0)
            {
                yield return (value.Index, value.Length);
            }
        }
    }

    /// <summary>
    /// Whether a run of base32 groups after an MFA cue is a key rather than words: one case throughout, and
    /// a digit of base32 (2 to 7) in it or all capitals. "type that into your" is four groups of four
    /// letters too. A random key of 16 characters has a digit in all but about 4 in 100.
    /// </summary>
    internal static bool LooksLikeSetupKey(ReadOnlySpan<char> run)
    {
        var (upper, lower, digit) = (false, false, false);
        foreach (var c in run)
        {
            upper |= char.IsAsciiLetterUpper(c);
            lower |= char.IsAsciiLetterLower(c);
            digit |= char.IsAsciiDigit(c);
        }

        return !(upper && lower) && (digit || upper);
    }

    /// <summary>The ABA routing number checksum: 3, 7 and 1 times the digits in turn, a multiple of ten.</summary>
    internal static bool PassesAbaChecksum(ReadOnlySpan<char> number)
    {
        if (number.Length != 9)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            if (!char.IsAsciiDigit(number[i]))
            {
                return false;
            }

            sum += (number[i] - '0') * (i % 3) switch { 0 => 3, 1 => 7, _ => 1 };
        }

        return sum % 10 == 0;
    }

    // A password in a web address's user information: "https://admin:value@host", "postgres://svc:value@10.0.0.5".
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9+.-]{1,30}://[^\s:/?#@]{1,256}:(?<v>[^\s/?#@]{1,256})@", RegexOptions.CultureInvariant, BuiltInTimeoutMs)]
    private static partial Regex UrlUserInfo();

    // "net use [Z:] \\server\share value": the password is the argument after the share.
    [GeneratedRegex(@"\bnet\s+use(?:\s+(?:[A-Za-z]:|\*))?\s+\\\\\S{1,260}\s+(?<v>(?![/*])\S{1,128})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, BuiltInTimeoutMs)]
    private static partial Regex NetUseAfterPath();

    // "net use \\server\share /user:name value": the password is the argument after the user.
    [GeneratedRegex(@"\bnet\s+use\s[^\r\n]{0,300}?\s/u(?:ser)?:\S{1,128}\s+(?<v>(?![/*])\S{1,128})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, BuiltInTimeoutMs)]
    private static partial Regex NetUseAfterUser();

    // "net user name value [/add]".
    [GeneratedRegex(@"\bnet\s+user\s+(?![/*])\S{1,128}\s+(?<v>(?![/*])\S{1,128})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, BuiltInTimeoutMs)]
    private static partial Regex NetUser();

    // "psexec \\host -u name -p value": -p only on a psexec line, since elsewhere it is a port or a path.
    [GeneratedRegex(@"\bpsexec(?:64)?(?:\.exe)?\b[^\r\n]{0,300}?\s-p\s+(?<v>(?!-)\S{1,128})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, BuiltInTimeoutMs)]
    private static partial Regex PsExecPassword();

    // "ConvertTo-SecureString [-String] 'value' -AsPlainText": the quoted value, straight or curly quotes.
    [GeneratedRegex(
        """\bConvertTo-SecureString\b(?:[^\r\n|;]{0,200}?-String)?\s+["'“”‘’](?<v>[^"'“”‘’\r\n]{1,256})["'“”‘’]""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        BuiltInTimeoutMs)]
    private static partial Regex SecureStringValue();

    // A BitLocker recovery key: eight groups of six digits, or after "recovery key" two or more of them
    // however they are separated, as one is read out.
    [GeneratedRegex(
        @"\b(?<v>\d{6}(?:[-‐–—]\d{6}){7})\b|(?i:\brecovery\s+(?:key|password)\b)[^\d\r\n]{0,40}(?<v>\d{6}(?:[-‐–—\s]{1,3}\d{6}){1,7})\b",
        RegexOptions.CultureInvariant,
        BuiltInTimeoutMs)]
    private static partial Regex RecoveryKey();

    // An MFA setup key: base32 in four or more groups of four, or one run, after the words an
    // authenticator's "can't scan" page puts before it.
    [GeneratedRegex(
        @"(?i:\b(?:secret\s+key|setup\s+key|can['’]?t\s+scan|cannot\s+scan|enter\s+(?:this|the)\s+(?:code|key)|manual(?:ly)?\s+entry(?:\s+key)?|key\s+for\s+manual\s+entry|totp\s+(?:key|secret)))[^\r\n]{0,60}?(?<![A-Za-z0-9])(?<v>[A-Za-z2-7]{4}(?:[ -]?[A-Za-z2-7]{4}){3,15})(?![A-Za-z0-9])",
        RegexOptions.CultureInvariant,
        BuiltInTimeoutMs)]
    private static partial Regex MfaSetupKey();

    // The secret of an otpauth address, which an authenticator's QR code holds as text.
    [GeneratedRegex(@"otpauth://\S{0,300}?[?&]secret=(?<v>[A-Z2-7]{16,128}=*)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, BuiltInTimeoutMs)]
    private static partial Regex OtpAuthSecret();

    // An ITIN, 9XX-XX-XXXX with the middle groups the IRS issues, on sight with a dash, or after its label
    // with any separator or none.
    [GeneratedRegex(
        @"\b9\d{2}[-‐–](?:5\d|6[0-5]|7\d|8[0-8]|9[0-2]|9[4-9])[-‐–]\d{4}\b"
        + @"|(?<=(?i:\bitin|individual\s{1,3}taxpayer\s{1,3}identification\s{1,3}number)(?i:\s{0,3}(?:number|no\.?|#))?\s{0,3}[:=#]?\s{0,3})9\d{2}[-‐–. ]?(?:5\d|6[0-5]|7\d|8[0-8]|9[0-2]|9[4-9])[-‐–. ]?\d{4}\b",
        RegexOptions.CultureInvariant,
        BuiltInTimeoutMs)]
    private static partial Regex Itin();

    // Beside its label only (RedactionPolicy.LabelledIds). Medicare's beneficiary identifier has a fixed
    // shape of digits and the letters it allows.
    [GeneratedRegex(
        @"(?i:\bmedicare(?:\s+(?:beneficiary\s+identifier|number|no\.?|#|id|card(?:\s+number)?))?)\s{0,3}[:#=]?\s{0,3}(?<v>[1-9][AC-HJKMNP-RT-Y][AC-HJKMNP-RT-Y0-9]\d-?[AC-HJKMNP-RT-Y][AC-HJKMNP-RT-Y0-9]\d-?[AC-HJKMNP-RT-Y]{2}\d{2})\b"
        + @"|\bMBI\s{0,3}[:#=]?\s{0,3}(?<v>[1-9][AC-HJKMNP-RT-Y][AC-HJKMNP-RT-Y0-9]\d-?[AC-HJKMNP-RT-Y][AC-HJKMNP-RT-Y0-9]\d-?[AC-HJKMNP-RT-Y]{2}\d{2})\b",
        RegexOptions.CultureInvariant,
        BuiltInTimeoutMs)]
    private static partial Regex MedicareNumber();

    [GeneratedRegex(@"(?i:\bpassport(?:\s*(?:number|no\.?|num|#|id))?)\s{0,3}[:#=]?\s{0,3}(?<v>[A-Z]{0,2}\d{6,9})\b", RegexOptions.CultureInvariant, BuiltInTimeoutMs)]
    private static partial Regex PassportNumber();

    // "Driver's License", "DL #", "DL No.", "DLN"; not "DL" alone, which is a server's model ("DL380").
    [GeneratedRegex(
        @"(?i:\bdriver['’]?s?\s+licen[cs]e(?:\s*(?:number|no\.?|num|#|id))?|\bDL\s*(?:#|no\.?|number|num)|\bDLN)\s{0,3}[:#=]?\s{0,3}(?<v>(?=[A-Za-z0-9-]{0,20}\d)[A-Za-z0-9][A-Za-z0-9-]{3,20}[A-Za-z0-9])\b",
        RegexOptions.CultureInvariant,
        BuiltInTimeoutMs)]
    private static partial Regex DriversLicence();

    [GeneratedRegex(@"(?i:\b(?:routing|ABA|RTN)(?:\s*(?:transit\s+)?(?:number|no\.?|num|#))?)\s{0,3}[:#=]?\s{0,3}(?<v>\d{9})\b", RegexOptions.CultureInvariant, BuiltInTimeoutMs)]
    private static partial Regex RoutingNumber();

    // A bank account number after a bank's word for it; "account number" alone is a PSA's own customer number.
    [GeneratedRegex(
        @"(?i:\b(?:bank|checking|chequing|savings|deposit)\s+(?:account|acct)\.?(?:\s*(?:number|no\.?|num|#))?)\s{0,3}[:#=]?\s{0,3}(?<v>\d(?:[ -]?\d){3,16})\b",
        RegexOptions.CultureInvariant,
        BuiltInTimeoutMs)]
    private static partial Regex BankAccount();
}
