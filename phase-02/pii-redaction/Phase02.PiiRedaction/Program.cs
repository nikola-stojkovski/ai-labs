using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Phase02.PiiRedaction;
using Phase02.PiiRedaction.Core;
using System.Globalization;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

using var host = LabHost.Create(args);
var model = host.Services.GetRequiredService<IChatClient>();
var metadata = model.GetService<ChatClientMetadata>();
Console.WriteLine($"Model: {metadata?.ProviderName} {metadata?.DefaultModelId}");

// Synthetic data only: the tool returns the employee's contact and bank details
var tool = AIFunctionFactory.Create(
    () => JsonSerializer.Serialize(new
    {
        Email = "ana.petrovska@example.com",
        Iban = "MK07250120000058984",
        Embg = "0101990450006",
    }),
    name: "GetEmployeeContact",
    description: "Gets the current employee's contact and bank details.");

const string systemPrompt = """
    You are PayPilot, a payroll assistant. Use the tools to look up the employee's data.
    Answer in at most 2 sentences.
    """;

const string question =
    "Hi, I'm Ana (ana.petrovska@example.com, EMBG 0101990450006). Which bank account will my salary be paid to?";

await RunAsync("Redactor OUTSIDE function invocation", streaming: false,
    builder => builder.UsePiiRedaction().UseFunctionInvocation());

await RunAsync("Redactor INSIDE function invocation", streaming: false,
    builder => builder.UseFunctionInvocation().UsePiiRedaction());

await RunAsync("Redactor INSIDE, streaming", streaming: true,
    builder => builder.UseFunctionInvocation().UsePiiRedaction());

async Task RunAsync(string title, bool streaming, Func<ChatClientBuilder, ChatClientBuilder> configure)
{
    RecordingChatClient? recorder = null;

    // The recorder is added last, so it is the innermost layer: it sees exactly what goes to the model.
    // Not disposed on purpose: disposing the pipeline would also dispose the shared model client.
    var pipeline = configure(model.AsBuilder())
        .Use(inner => recorder = new RecordingChatClient(inner))
        .Build(host.Services);

    List<ChatMessage> messages =
    [
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, question),
    ];
    var options = new ChatOptions { Tools = [tool], Temperature = 0 };

    Console.WriteLine($"\n=== {title} ===");

    string answer;
    if (streaming)
    {
        var text = new StringBuilder();
        await foreach (var update in pipeline.GetStreamingResponseAsync(messages, options))
        {
            text.Append(update.Text);
        }

        answer = text.ToString();
    }
    else
    {
        answer = (await pipeline.GetResponseAsync(messages, options)).Text;
    }

    Console.WriteLine($"Answer: {answer}");
    Console.WriteLine($"Requests that reached the model: {recorder!.Requests.Count}");

    for (var i = 0; i < recorder.Requests.Count; i++)
    {
        var request = recorder.Requests[i];
        var leaks = request
            .SelectMany(message => message.Contents)
            .Select(Describe)
            .Where(PiiRedactor.ContainsPii)
            .ToList();

        Console.WriteLine(
            $"  Request {i + 1}: {request.Count} messages, " +
            (leaks.Count == 0 ? "clean" : $"LEAKED in {leaks.Count} item(s)"));

        foreach (var leak in leaks)
        {
            Console.WriteLine($"    {leak}");
        }
    }
}

static string Describe(AIContent content) => content switch
{
    TextContent text => text.Text,
    FunctionResultContent result => result.Result switch
    {
        JsonElement json => json.GetRawText(),
        var other => other?.ToString() ?? string.Empty,
    },
    _ => string.Empty,
};