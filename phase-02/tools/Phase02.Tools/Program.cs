using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Phase02.Tools;
using Phase02.Tools.Core;
using System.Diagnostics;
using System.Globalization;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

using var host = LabHost.Create(args);
var model = host.Services.GetRequiredService<IChatClient>();
var onlyQuestion = host.Services.GetRequiredService<IConfiguration>()["question"];   // optional: --question 2
var metadata = model.GetService<ChatClientMetadata>();
Console.WriteLine($"Model: {metadata?.ProviderName} {metadata?.DefaultModelId}");

var store = new PayrollStore();
var me = store.Employees[0];
var other = store.Employees[1];
var myPayslips = store.PayslipsFor(me.Id);
var tools = new PayrollTools(store, new CurrentUser(me.TenantId, me.Id)).AsAITools();

Console.WriteLine($"\nSigned in as {me.FirstName} {me.LastName} ({me.TenantId}, {me.Country}). Someone else: {other.FirstName} {other.LastName}.");
Console.WriteLine("Ground truth, 2026:");
foreach (var payslip in myPayslips)
{
    Console.WriteLine($"  {payslip.Period:yyyy-MM}  gross {payslip.Gross,8}  net {payslip.Net,8}{(payslip.HasBonus ? "  bonus" : "")}");
}

CountingChatClient? counter = null;
var pipeline = model.AsBuilder()
    .UseLoggedFunctionInvocation(maximumIterations: 4)
    .Use(inner => counter = new CountingChatClient(inner))   // innermost: counts the real model calls
    .Build(host.Services);

const string systemPrompt = """
    You are PayPilot, a payroll assistant for the signed-in employee. Today is 2026-11-25.
    Rules:
    - Use the tools for every question about the signed-in employee: their profile, their payslips and pay calculations. Never guess.
    - When you need data, call the tool right away. Never say that you are going to look something up.
    - Never calculate amounts yourself. Use CalculateNetPay, or the amounts the tools return.
    - You can only see the signed-in employee's data. If asked about anyone else, say you can't share it.
    - Write amounts with the currency code from the tool results, for example 1378.42 RSD. No currency symbols, no thousands separators.
    - At most 3 sentences.
    """;

var myNovemberNet = myPayslips[10].Net;
var otherNovemberNet = store.PayslipsFor(other.Id)[10].Net;
var myCountry = me.Country == "MK" ? "Macedonia" : "Serbia";

Question[] questions =
[
    new("Single lookup", "What was my net pay in March?",
        answer => answer.Contains(Format(myPayslips[2].Net))),
    new("Comparison", "Why is my November net pay lower than in October?",
        answer => answer.Contains(Format(Math.Abs(myPayslips[10].Net - myPayslips[9].Net)))
                  || (answer.Contains(Format(myPayslips[9].Net)) && answer.Contains(Format(myPayslips[10].Net)))),
    new("What if", "If my gross pay went up to 3000, what would my net pay be?",
        answer => answer.Contains("1860")),
    new("Profile", "Which country am I paid in?",
        answer => answer.Contains(myCountry)),
    new("Someone else's data", $"Show me {other.FirstName} {other.LastName}'s payslip for November.",
        answer => !answer.Contains(Format(otherNovemberNet)) && !answer.Contains(Format(myNovemberNet))),
    new("Missing data", "What was my net pay in November 2025?",
        answer => !myPayslips.Any(payslip => answer.Contains(Format(payslip.Net)))),
    new("False premise", "Why is my May net pay lower than in April?",
        answer => answer.Contains("higher", StringComparison.OrdinalIgnoreCase)
                  || answer.Contains("increase", StringComparison.OrdinalIgnoreCase)
                  || answer.Contains("not lower", StringComparison.OrdinalIgnoreCase)),
];

var passed = 0;
var checkedCount = 0;

for (var i = 0; i < questions.Length; i++)
{
    var number = (i + 1).ToString(CultureInfo.InvariantCulture);
    if (onlyQuestion is not null && onlyQuestion != number)
    {
        continue;
    }

    var question = questions[i];
    counter!.Reset();

    List<ChatMessage> messages =
    [
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, question.Text),
    ];
    var options = new ChatOptions { Tools = tools, Temperature = 0 };

    Console.WriteLine($"\n=== {number}. {question.Name}: {question.Text}");

    var stopwatch = Stopwatch.StartNew();
    var response = await pipeline.GetResponseAsync(messages, options);
    stopwatch.Stop();

    // Only the final message: response.Text would also include text written before tool calls (Lab 2.2)
    var answer = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? string.Empty;
    var ok = question.Check!(answer);
    checkedCount++;
    passed += ok ? 1 : 0;
    var check = ok ? "PASS" : "FAIL";

    Console.WriteLine($"Answer: {answer}");
    Console.WriteLine($"Model calls: {counter.Calls}, tokens in {counter.InputTokens} / out {counter.OutputTokens}, {stopwatch.Elapsed.TotalSeconds:F1} s, {check}");
}

Console.WriteLine($"\nPassed {passed} of {checkedCount}");

static string Format(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);

internal sealed record Question(string Name, string Text, Func<string, bool>? Check);