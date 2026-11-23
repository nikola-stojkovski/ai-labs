namespace Phase02.ProviderSwitch.Core;

public sealed record ExplainResult(string Answer, string? ModelId, long? InputTokens, long? OutputTokens);