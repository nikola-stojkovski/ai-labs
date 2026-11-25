using Microsoft.Extensions.AI;

namespace Phase02.Tools;

/// <summary>
/// Innermost layer for experiments: counts the real model calls and tokens behind one question.
/// </summary>
public sealed class CountingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public int Calls { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }

    public void Reset()
    {
        Calls = 0;
        InputTokens = 0;
        OutputTokens = 0;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        Calls++;
        InputTokens += response.Usage?.InputTokenCount ?? 0;
        OutputTokens += response.Usage?.OutputTokenCount ?? 0;

        return response;
    }

    // Counts streaming calls too; token counts are only read on the non-streaming path this lab uses
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }
}