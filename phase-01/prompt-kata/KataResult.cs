namespace Phase01.PromptKata;

public sealed record KataResult(
    string Prompt,
    string Model,
    float Temperature,
    int Sample,
    string Answer,
    bool? Passed,
    double Seconds,
    long OutputTokens);