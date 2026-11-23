namespace Labs.Common;

public enum AiProvider
{
    Ollama,
    OpenAI,
    Fake,
}

public sealed class LabAiOptions
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.Ollama;

    // Ollama's own API (OllamaSharp)
    public string Endpoint { get; set; } = "http://localhost:11434";

    // Any OpenAI-compatible API: OpenAI, Azure OpenAI / Foundry, or Ollama's /v1
    public string OpenAiEndpoint { get; set; } = "http://localhost:11434/v1";

    // Ollama ignores the key, but the OpenAI SDK requires one. Real keys go in user secrets, never here.
    public string ApiKey { get; set; } = "ollama";

    public string ChatModel { get; set; } = "qwen3:4b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}
