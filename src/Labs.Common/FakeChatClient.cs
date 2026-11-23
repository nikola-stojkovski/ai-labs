using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Labs.Common;

/// <summary>
/// An IChatClient with no model behind it: returns a fixed _answer and remembers what it was sent.
/// For tests and for running business code offline.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly string _answer;

    public FakeChatClient(string answer = "This is a fake answer.")
    {
        _answer = answer;
    }

    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Capture(messages, options);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _answer))
        {
            ModelId = "fake",
            Usage = new UsageDetails { InputTokenCount = 0, OutputTokenCount = 0 },
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Capture(messages, options);

        var words = _answer.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, i == 0 ? words[i] : " " + words[i]);
            await Task.Yield();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("fake", null, "fake")
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private void Capture(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        LastMessages = messages.ToList();
        LastOptions = options;
    }
}