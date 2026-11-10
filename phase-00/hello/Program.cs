using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

using var host = LabHost.Create(args);
var chat = host.Services.GetRequiredService<IChatClient>();

var employee = SyntheticPayroll.Employees(count: 1).Single();
var payslip = SyntheticPayroll.Payslips(employee).First();

var question =
    $"/no_think\nIn two sentences, explain to {employee.FirstName} why their gross pay of " +
    $"{payslip.Gross} became a net pay of {payslip.Net}.";

Console.WriteLine($"Q: {question}\n");

var stopwatch = Stopwatch.StartNew();
UsageDetails? usage = null;

await foreach (var update in chat.GetStreamingResponseAsync(question))
{
    Console.Write(update.Text);

    foreach (var reasoning in update.Contents.OfType<TextReasoningContent>())
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Write(reasoning.Text);
        Console.ResetColor();
    }

    foreach (var content in update.Contents.OfType<UsageContent>())
        usage = content.Details;
}

stopwatch.Stop();
Console.WriteLine();
Console.WriteLine();
Console.WriteLine($"Time: {stopwatch.Elapsed.TotalSeconds:F1} s");
Console.WriteLine($"Input tokens:  {usage?.InputTokenCount?.ToString() ?? "n/a"}");
Console.WriteLine($"Output tokens: {usage?.OutputTokenCount?.ToString() ?? "n/a"}");