using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Phase02.PiiRedaction.Core;
using System.Text.Json;
using Xunit;

namespace Phase02.PiiRedaction.Tests;

public class PiiRedactingChatClientTests
{
    private const string UserText = "My email is ana.petrovska@example.com and my EMBG is 0101990450006.";

    private static PiiRedactingChatClient CreateClient(FakeChatClient fake) =>
        new(fake, NullLogger<PiiRedactingChatClient>.Instance);

    [Fact]
    public async Task Masks_user_text_before_it_reaches_the_inner_client()
    {
        using var fake = new FakeChatClient();
        using var client = CreateClient(fake);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, UserText)], cancellationToken: TestContext.Current.CancellationToken);

        var sent = Assert.Single(fake.LastMessages);
        Assert.Equal("My email is [EMAIL] and my EMBG is [EMBG].", sent.Text);
    }

    [Fact]
    public async Task Masks_tool_results()
    {
        using var fake = new FakeChatClient();
        using var client = CreateClient(fake);
        var toolResult = JsonSerializer.SerializeToElement(new { Iban = "MK07250120000058984", Email = "ana.petrovska@example.com" });

        List<ChatMessage> history =
        [
            new(ChatRole.User, "Where does my salary go?"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetEmployeeContact")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", toolResult)]),
        ];

        await client.GetResponseAsync(history, cancellationToken: TestContext.Current.CancellationToken);

        var result = fake.LastMessages[2].Contents.OfType<FunctionResultContent>().Single();
        var text = Assert.IsType<string>(result.Result);
        Assert.False(PiiRedactor.ContainsPii(text));
        Assert.Contains("[IBAN]", text);
        Assert.Equal("call-1", result.CallId);
    }

    [Fact]
    public async Task Masks_on_the_streaming_path_too()
    {
        using var fake = new FakeChatClient();
        using var client = CreateClient(fake);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, UserText)],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.False(PiiRedactor.ContainsPii(fake.LastMessages.Single().Text));
    }

    [Fact]
    public async Task Does_not_change_the_callers_history()
    {
        using var fake = new FakeChatClient();
        using var client = CreateClient(fake);
        var original = new ChatMessage(ChatRole.User, UserText);

        await client.GetResponseAsync([original], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UserText, original.Text);
        Assert.NotSame(original, fake.LastMessages.Single());
    }

    [Fact]
    public async Task Passes_clean_messages_through_unchanged()
    {
        using var fake = new FakeChatClient();
        using var client = CreateClient(fake);
        var clean = new ChatMessage(ChatRole.User, "Why is my net pay lower?");

        await client.GetResponseAsync([clean], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(clean, fake.LastMessages.Single());
    }
}