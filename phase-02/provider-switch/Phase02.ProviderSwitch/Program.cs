using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Phase02.ProviderSwitch.Core;
using System.Diagnostics;
using System.Globalization;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

using var host = LabHost.Create(args, builder => builder.Services.AddTransient<PayslipExplainer>());

var ai = host.Services.GetRequiredService<IConfiguration>()
             .GetSection(LabAiOptions.SectionName).Get<LabAiOptions>() ?? new LabAiOptions();
var chat = host.Services.GetRequiredService<IChatClient>();
var explainer = host.Services.GetRequiredService<PayslipExplainer>();

// GetService: ask the client what's underneath it, without casting to a provider type
var metadata = chat.GetService<ChatClientMetadata>();
Console.WriteLine($"Provider (config): {ai.Provider}");
Console.WriteLine($"Client reports:    {metadata?.ProviderName ?? "?"} at {metadata?.ProviderUri?.ToString() ?? "n/a"}, model {metadata?.DefaultModelId ?? "?"}");

var employee = SyntheticPayroll.Employees(count: 1).Single();
var source = SyntheticPayroll.Payslips(employee)[0];
var payslip = new PayslipAmounts(source.Gross, source.IncomeTax, source.SocialContributions, source.Net);

Console.WriteLine("\nLoading the model...");
var warmUp = Stopwatch.StartNew();
await chat.GetResponseAsync("Hi", new ChatOptions { MaxOutputTokens = 1 });
Console.WriteLine($"Warm-up: {warmUp.Elapsed.TotalSeconds:F1} s");

const string question = "Why is my net pay lower than my gross pay?";

for (var run = 1; run <= 3; run++)
{
    var stopwatch = Stopwatch.StartNew();
    var result = await explainer.ExplainNetPayAsync(payslip, question);
    stopwatch.Stop();

    Console.WriteLine(
        $"\n#{run}  {stopwatch.Elapsed.TotalSeconds:F1} s" +
        $"  in {result.InputTokens?.ToString() ?? "n/a"}" +
        $"  out {result.OutputTokens?.ToString() ?? "n/a"}" +
        $"  model {result.ModelId ?? "n/a"}");
    Console.WriteLine(result.Answer);
}