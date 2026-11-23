using System.Text.RegularExpressions;

namespace Phase02.PiiRedaction.Core;

/// <summary>
/// Masks personal data in text. Pure function: no I/O, no state, easy to test.
/// </summary>
public static partial class PiiRedactor
{
    // IBAN: 2 letters, 2 check digits, 11-30 letters/digits, spaces allowed between them.
    // Runs first, so the digits inside an IBAN are never mistaken for an EMBG.
    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?: ?[A-Z0-9]){11,30}\b")]
    private static partial Regex Iban();

    // EMBG: 13 digits that start with a valid day (01-31) and month (01-12).
    // The date check removes most random 13-digit numbers (order IDs, references).
    [GeneratedRegex(@"\b(?:0[1-9]|[12]\d|3[01])(?:0[1-9]|1[0-2])\d{9}\b")]
    private static partial Regex Embg();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    private static readonly (string Kind, Func<Regex> Pattern)[] Rules =
    [
        ("IBAN", Iban),
        ("EMBG", Embg),
        ("EMAIL", Email),
    ];

    /// <summary>
    /// Replaces every match with [KIND] and adds what it masked to <paramref name="counts"/>.
    /// Only kinds and counts are recorded, never the values.
    /// </summary>
    public static string Redact(string text, Dictionary<string, int> counts)
    {
        foreach (var (kind, pattern) in Rules)
        {
            text = pattern().Replace(text, _ =>
            {
                counts.TryGetValue(kind, out var count);
                counts[kind] = count + 1;
                return $"[{kind}]";
            });
        }

        return text;
    }

    public static bool ContainsPii(string text) => Rules.Any(rule => rule.Pattern().IsMatch(text));
}