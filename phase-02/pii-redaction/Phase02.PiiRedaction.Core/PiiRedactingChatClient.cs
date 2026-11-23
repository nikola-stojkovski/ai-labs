using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Phase02.PiiRedaction.Core;

/// <summary>
/// Masks personal data in every message before the request goes further down the pipeline.
/// Builds new messages: the caller's history is never changed.
/// </summary>
public sealed class PiiRedactingChatClient(IChatClient innerClient, ILogger<PiiRedactingChatClient> logger)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Redact(messages), options, cancellationToken);

    // Without this override the streaming path (PayPilot's SSE endpoint) would skip redaction silently
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Redact(messages), options, cancellationToken);

    private List<ChatMessage> Redact(IEnumerable<ChatMessage> messages)
    {
        var counts = new Dictionary<string, int>();
        var redacted = messages.Select(message => RedactMessage(message, counts)).ToList();

        if (counts.Count > 0)
        {
            // Kinds and counts only. Logging the values would copy the personal data into the logs.
            logger.LogInformation(
                "Masked before sending: {Masked}",
                string.Join(", ", counts.Select(pair => $"{pair.Value} {pair.Key}")));
        }

        return redacted;
    }

    private static ChatMessage RedactMessage(ChatMessage message, Dictionary<string, int> counts)
    {
        var changed = false;
        var contents = new List<AIContent>(message.Contents.Count);

        foreach (var content in message.Contents)
        {
            var redacted = content switch
            {
                TextContent text => RedactText(text, counts),
                FunctionResultContent result => RedactToolResult(result, counts),
                _ => content,
            };

            changed |= !ReferenceEquals(redacted, content);
            contents.Add(redacted);
        }

        if (!changed)
        {
            return message;
        }

        return new ChatMessage(message.Role, contents)
        {
            AuthorName = message.AuthorName,
            MessageId = message.MessageId,
            AdditionalProperties = message.AdditionalProperties,
        };
    }

    private static AIContent RedactText(TextContent content, Dictionary<string, int> counts)
    {
        var text = PiiRedactor.Redact(content.Text, counts);
        return text == content.Text ? content : new TextContent(text);
    }

    private static AIContent RedactToolResult(FunctionResultContent content, Dictionary<string, int> counts)
    {
        // A tool can return anything: a string, JSON, or an object. Redact its JSON text.
        var raw = content.Result switch
        {
            null => null,
            string text => text,
            JsonElement json => json.GetRawText(),
            var other => JsonSerializer.Serialize(other, AIJsonUtilities.DefaultOptions),
        };

        if (raw is null)
        {
            return content;
        }

        var redacted = PiiRedactor.Redact(raw, counts);
        return redacted == raw ? content : new FunctionResultContent(content.CallId, redacted);
    }
}