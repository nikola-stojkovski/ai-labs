using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.Tokenizers;
using Phase01.CostCalculator;
using System.Diagnostics;
using System.Text.Json;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const int AnswerTokens = 150;          // a short visible answer
const int ReasoningTokens = 400;       // hidden thinking, roughly what qwen3:4b produced in phase-00/hello
const int RequestsPerMonth = 100_000;

const int Turns = 40;
const int OllamaDefaultContext = 4096;

using var host = LabHost.Create(args);
var config = host.Services.GetRequiredService<IConfiguration>();
var pricing = config.GetSection(PricingOptions.SectionName).Get<PricingOptions>()
              ?? throw new InvalidOperationException("Missing 'Pricing' section in appsettings.json.");

// o200k_base is the tokenizer of OpenAI's recent models. For qwen3 it is only an estimate.
var tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");

var employee = SyntheticPayroll.Employees(count: 1).Single();
var payslips = SyntheticPayroll.Payslips(employee);
var payslip = payslips[0];

// ---------------------------------------------------------------------------
Header("1. Same meaning, different token counts");

(string Label, string Text)[] samples =
[
    ("English sentence",
     "Your gross pay was 2,450.00. We deducted 245.00 income tax and 686.00 social contributions, so your net pay is 1,519.00."),
    ("Macedonian sentence",
     "Вашата бруто плата изнесува 2.450,00. Одбивме 245,00 за персонален данок на доход и 686,00 за социјални придонеси, па вашата нето плата е 1.519,00."),
    ("Payslip as JSON", JsonSerializer.Serialize(payslip)),
    ("Payslip as compact text",
     $"gross={payslip.Gross} tax={payslip.IncomeTax} social={payslip.SocialContributions} net={payslip.Net}"),
];

Console.WriteLine($"{"Text",-26}{"Chars",8}{"Tokens",8}{"Chars/token",13}");
foreach (var (label, text) in samples)
{
    var tokens = tokenizer.CountTokens(text);
    Console.WriteLine($"{label,-26}{text.Length,8}{tokens,8}{(double)text.Length / tokens,13:F1}");
}

foreach (var text in new[] { "Your gross pay", "Вашата бруто плата" })
{
    var pieces = tokenizer.EncodeToTokens(text, out _);
    Console.WriteLine(string.Join(" | ", pieces.Select(p => p.Value)));
}

// ---------------------------------------------------------------------------
Header("2. Cost of one PayPilot request");

var systemPrompt = """
    You are PayPilot, a payroll assistant for employees and payroll admins.
    Answer only questions about payslips, pay and payroll policy.
    Use only the payslip data you are given. If the data does not contain the answer, say you don't know.
    Keep answers short.
    """;
var question = $"Why is my net pay {payslip.Net} when my gross pay is {payslip.Gross}?";
var userMessage = $"{JsonSerializer.Serialize(payslip)}\n\n{question}";
var inputTokens = tokenizer.CountTokens(systemPrompt) + tokenizer.CountTokens(userMessage);

Console.WriteLine($"Input: {inputTokens} tokens (o200k estimate). Output: {AnswerTokens} answer tokens, " +
                  $"plus {ReasoningTokens} if the model reasons first.");
Console.WriteLine($"Prices in {pricing.Currency} per 1M tokens, checked on {pricing.CheckedOn}.");
Console.WriteLine();
Console.WriteLine($"{"Model",-16}{"Per request",14}{"+ reasoning",14}{$"{RequestsPerMonth:N0}/month",16}{"+ reasoning",14}");

foreach (var model in pricing.Models)
{
    var plain = model.Cost(inputTokens, AnswerTokens);
    var reasoning = model.Cost(inputTokens, AnswerTokens + ReasoningTokens);
    Console.WriteLine($"{model.Name,-16}{plain,14:F6}{reasoning,14:F6}" +
                      $"{plain * RequestsPerMonth,16:N2}{reasoning * RequestsPerMonth,14:N2}");
}

// ---------------------------------------------------------------------------
Header($"3. A {Turns}-turn chat: every turn resends the whole conversation");

// Each turn the user asks about another month, and the app attaches that month's payslip.
var cheapest = pricing.Models.MinBy(m => m.InputPerMillion)
               ?? throw new InvalidOperationException("No models in 'Pricing:Models'.");
var history = tokenizer.CountTokens(systemPrompt);
long totalInput = 0, totalOutput = 0;
int? firstTurnOverLimit = null;

Console.WriteLine($"{"Turn",5}{"Input tokens",15}{"Total input",14}{$"Total cost ({cheapest.Name})",28}");

for (var turn = 1; turn <= Turns; turn++)
{
    var slip = payslips[(turn - 1) % payslips.Count];
    var turnQuestion = $"{JsonSerializer.Serialize(slip)}\n\nExplain my {slip.Period:MMMM} payslip.";
    var turnAnswer = $"In {slip.Period:MMMM} your gross pay was {slip.Gross}. We deducted {slip.IncomeTax} income tax " +
                     $"and {slip.SocialContributions} social contributions, so your net pay was {slip.Net}.";

    var turnInput = history + tokenizer.CountTokens(turnQuestion);
    var turnOutput = tokenizer.CountTokens(turnAnswer);

    totalInput += turnInput;
    totalOutput += turnOutput;
    history = turnInput + turnOutput;                  // the next turn resends all of this

    if (turnInput > OllamaDefaultContext)
        firstTurnOverLimit ??= turn;

    if (turn == 1 || turn % 10 == 0)
        Console.WriteLine($"{turn,5}{turnInput,15:N0}{totalInput,14:N0}{cheapest.Cost(totalInput, totalOutput),28:F5}");
}

Console.WriteLine();
Console.WriteLine(firstTurnOverLimit is { } overLimit
    ? $"From turn {overLimit}, the input is over Ollama's default {OllamaDefaultContext}-token context: the oldest messages get cut."
    : $"All {Turns} turns fit in Ollama's default {OllamaDefaultContext}-token context.");

// ---------------------------------------------------------------------------
if (string.Equals(config["mode"], "offline", StringComparison.OrdinalIgnoreCase))
    return;

var modelName = (config.GetSection(LabAiOptions.SectionName).Get<LabAiOptions>() ?? new LabAiOptions()).ChatModel;
Header($"4. Live: {modelName} on Ollama");

var chat = host.Services.GetRequiredService<IChatClient>();
List<ChatMessage> messages =
[
    new(ChatRole.System, systemPrompt),
    new(ChatRole.User, userMessage),
];

// Warm-up: the first call loads the model into memory. That's a separate cost from answering.
var stopwatch = Stopwatch.StartNew();
await chat.GetResponseAsync("Hi", new ChatOptions { MaxOutputTokens = 1 });
var loadTime = stopwatch.Elapsed;

// Streaming: measure when the first token arrives, and when the first token of the real answer arrives.
stopwatch.Restart();
TimeSpan? firstToken = null, firstAnswerToken = null;
UsageDetails? streamingUsage = null;
var inThink = false;

await foreach (var update in chat.GetStreamingResponseAsync(messages))
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case TextReasoningContent:
                firstToken ??= stopwatch.Elapsed;
                break;

            case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                firstToken ??= stopwatch.Elapsed;
                if (text.Text.Contains("<think>")) inThink = true;
                if (text.Text.Contains("</think>")) { inThink = false; break; }
                if (!inThink) firstAnswerToken ??= stopwatch.Elapsed;
                break;

            case UsageContent usage:
                streamingUsage = usage.Details;
                break;
        }
    }
}

var streamingTotal = stopwatch.Elapsed;

// Non-streaming: the user sees nothing until the whole answer is ready.
stopwatch.Restart();
var response = await chat.GetResponseAsync(messages);
var nonStreamingTotal = stopwatch.Elapsed;

Console.WriteLine($"Model load (warm-up):  {loadTime.TotalSeconds,6:F1} s");
Console.WriteLine();
Console.WriteLine($"Input tokens, o200k estimate: {inputTokens}");
Console.WriteLine($"Input tokens, Ollama actual:  {streamingUsage?.InputTokenCount?.ToString() ?? "n/a"}");
Console.WriteLine();
Console.WriteLine($"{"",-15}{"First token",13}{"First answer",14}{"Total",9}{"Output tokens",15}");
Console.WriteLine($"{"Streaming",-15}{Seconds(firstToken),13}{Seconds(firstAnswerToken),14}" +
                  $"{Seconds(streamingTotal),9}{streamingUsage?.OutputTokenCount?.ToString() ?? "n/a",15}");
Console.WriteLine($"{"Non-streaming",-15}{Seconds(nonStreamingTotal),13}{Seconds(nonStreamingTotal),14}" +
                  $"{Seconds(nonStreamingTotal),9}{response.Usage?.OutputTokenCount?.ToString() ?? "n/a",15}");
Console.WriteLine();
Console.WriteLine("Answer (non-streaming):");
Console.WriteLine(response.Text);

static void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
}

static string Seconds(TimeSpan? time) => time is { } t ? $"{t.TotalSeconds:F1} s" : "n/a";