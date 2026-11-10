namespace Labs.Common;

public sealed class LabAiOptions
{
    public const string SectionName = "Ai";

    public string Endpoint { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen3:4b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}
