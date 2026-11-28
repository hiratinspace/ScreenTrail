using System.Text.RegularExpressions;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Privacy;

/// <summary>
/// Which patterns the engine looks for (Spec S6 toggles), plus the technician's own. The tenant's policy has
/// no redaction fields yet; until it does, an installed build keeps the four built-in rules on whatever the
/// settings say (DEC-06, <c>PolicyApplier</c>), and a save over the pipe that changes a switch needs a token.
/// </summary>
public sealed record RedactionPolicy
{
    /// <summary>Longest a single pattern may spend on one piece of text. A custom pattern cannot hang capture.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public bool Ssn { get; init; } = true;

    public bool Cards { get; init; } = true;

    public bool ApiKeys { get; init; } = true;

    public bool Passwords { get; init; } = true;

    /// <summary>Off by default: an email address is usually the ticket's own contact, not a secret.</summary>
    public bool Emails { get; init; }

    /// <summary>
    /// United States ids that have no shape safe to mask on sight, masked only where their label sits
    /// beside them: Medicare, passport, driver's licence, bank routing (its checksum held) and bank
    /// account numbers (DEC-08). Off by default until the laptop measures how much honest text each one
    /// masks; nothing turns it on yet but a test.
    /// </summary>
    public bool LabelledIds { get; init; }

    /// <summary>The technician's own regexes from Settings, already validated by <see cref="ValidateCustomPattern"/>.</summary>
    public IReadOnlyList<string> CustomPatterns { get; init; } = [];

    public static RedactionPolicy Default { get; } = new();

    /// <summary>Everything on, including emails: what "maximum redaction" means in Settings.</summary>
    public static RedactionPolicy Strict { get; } = new() { Emails = true };

    /// <summary>
    /// Checks a tenant regex for the Settings validator and the policy sync. Returns null when it's usable,
    /// otherwise a message to show the admin.
    /// </summary>
    public static string? ValidateCustomPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "Enter a pattern.";
        }

        try
        {
            var regex = new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);

            // A pattern that matches nothing at all would blank every frame it touches.
            if (regex.IsMatch(string.Empty))
            {
                return "This pattern matches empty text, which would redact everything.";
            }
        }
        catch (ArgumentException ex)
        {
            return $"Not a valid regular expression: {ex.Message}";
        }

        return null;
    }

    public bool IsEnabled(MaskKind kind) => kind switch
    {
        MaskKind.Ssn => Ssn,
        MaskKind.Card => Cards,
        MaskKind.ApiKey => ApiKeys,
        MaskKind.Password => Passwords,
        MaskKind.Email => Emails,
        MaskKind.CustomPattern => CustomPatterns.Count > 0,
        _ => false,
    };
}
