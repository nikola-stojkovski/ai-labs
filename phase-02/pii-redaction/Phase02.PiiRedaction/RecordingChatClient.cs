using Microsoft.Extensions.AI;

namespace Phase02.PiiRedaction;

/// <summary>
/// Innermost layer for experiments: keeps a copy of every request that goes to the model.
/// </summary>
public sealed class RecordingChatClient : DelegatingChatClient
{
    public List<List<ChatMessage>> Requests { get; } = [];

    private readonly IChatClient _innerClient;

    public RecordingChatClient(IChatClient innerClient) : base(innerClient)
    {
        _innerClient = innerClient;
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Record(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Record(messages), options, cancellationToken);

    private List<ChatMessage> Record(IEnumerable<ChatMessage> messages)
    {
        var copy = messages.ToList();
        Requests.Add(copy);
        return copy;
    }
}
