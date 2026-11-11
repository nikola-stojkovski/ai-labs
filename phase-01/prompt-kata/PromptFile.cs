namespace Phase01.PromptKata;

public sealed record PromptFile(string Name, string SystemPrompt, string UserPrompt, string? Expected)
{
    public static PromptFile Load(string path)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("## "))
            {
                current = [];
                sections[line[3..].Trim()] = current;
            }
            else
            {
                current?.Add(line);
            }
        }

        string? Section(string name) =>
            sections.TryGetValue(name, out var lines) ? string.Join('\n', lines).Trim() : null;

        return new PromptFile(
            Path.GetFileNameWithoutExtension(path),
            Section("system") ?? throw new InvalidDataException($"{path}: missing '## system' section."),
            Section("user") ?? throw new InvalidDataException($"{path}: missing '## user' section."),
            Section("expected"));
    }

    public PromptFile Fill(IReadOnlyDictionary<string, string> values)
    {
        string Apply(string text) =>
            values.Aggregate(text, (result, value) => result.Replace("{{" + value.Key + "}}", value.Value));

        return this with
        {
            SystemPrompt = Apply(SystemPrompt),
            UserPrompt = Apply(UserPrompt),
            Expected = Expected is null ? null : Apply(Expected),
        };
    }
}