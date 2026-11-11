using System.Text.RegularExpressions;

namespace Phase01.PromptKata;

public static class AnswerCheck
{
    // null = nothing to check automatically; judge the answer by reading it.
    public static bool? Passes(string answer, string? expected)
    {
        if (expected is null)
            return null;

        var clean = Clean(answer);

        if (expected.StartsWith("equals:", StringComparison.OrdinalIgnoreCase))
            return string.Equals(NoSpaces(clean), NoSpaces(expected["equals:".Length..]), StringComparison.OrdinalIgnoreCase);

        if (expected.StartsWith("contains:", StringComparison.OrdinalIgnoreCase))
            return expected["contains:".Length..]
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .All(part => clean.Contains(part, StringComparison.OrdinalIgnoreCase));

        throw new InvalidDataException($"Unknown check '{expected}'. Use 'equals:' or 'contains:'.");
    }

    // Removes reasoning blocks and Markdown code fences that models add around their answers.
    public static string Clean(string answer) =>
        Regex.Replace(answer, @"<think>[\s\S]*?</think>", "")
             .Replace("```json", "")
             .Replace("```", "")
             .Trim();

    private static string NoSpaces(string text) => Regex.Replace(text, @"\s+", "");
}