using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using Phase01.PromptKata;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;   // "0.7", never "0,7", everywhere in the output

using var host = LabHost.Create(args);
var config = host.Services.GetRequiredService<IConfiguration>();
var ai = config.GetSection(LabAiOptions.SectionName).Get<LabAiOptions>() ?? new LabAiOptions();
var kata = config.GetSection(KataOptions.SectionName).Get<KataOptions>()
           ?? throw new InvalidOperationException("Missing 'Kata' section in appsettings.json.");
var only = config["only"];   // optional: --only qwen3:4b-instruct runs one model

var employee = SyntheticPayroll.Employees(count: 1).Single();
var payslip = SyntheticPayroll.Payslips(employee)[0];

var values = new Dictionary<string, string>
{
    ["payslip"] = JsonSerializer.Serialize(payslip),
    ["gross"] = payslip.Gross.ToString(CultureInfo.InvariantCulture),
    ["tax"] = payslip.IncomeTax.ToString(CultureInfo.InvariantCulture),
    ["social"] = payslip.SocialContributions.ToString(CultureInfo.InvariantCulture),
    ["net"] = payslip.Net.ToString(CultureInfo.InvariantCulture),
};

var prompts = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "prompts"), "*.md")
    .Order()
    .Select(path => PromptFile.Load(path).Fill(values))
    .ToList();

// Same long timeout as Labs.Common: the thinking model can take minutes per answer on CPU.
var http = new HttpClient { BaseAddress = new Uri(ai.Endpoint), Timeout = TimeSpan.FromMinutes(10) };
var results = new List<KataResult>();

foreach (var run in kata.Runs.Where(r => only is null || r.Model == only))
{
    IChatClient chat = new OllamaApiClient(http, run.Model);

    Console.WriteLine($"\n{run.Model}: loading the model...");
    await chat.GetResponseAsync("Hi", new ChatOptions { MaxOutputTokens = 1 });

    foreach (var prompt in prompts)
    {
        foreach (var temperature in kata.Temperatures)
        {
            for (var sample = 1; sample <= run.Samples; sample++)
            {
                List<ChatMessage> messages =
                [
                    new(ChatRole.System, prompt.SystemPrompt),
                    new(ChatRole.User, prompt.UserPrompt),
                ];

                var stopwatch = Stopwatch.StartNew();
                var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = temperature });
                stopwatch.Stop();

                var result = new KataResult(
                    prompt.Name, run.Model, temperature, sample,
                    AnswerCheck.Clean(response.Text),
                    AnswerCheck.Passes(response.Text, prompt.Expected),
                    stopwatch.Elapsed.TotalSeconds,
                    response.Usage?.OutputTokenCount ?? 0);

                results.Add(result);
                Console.WriteLine($"  {prompt.Name,-34} t={temperature:0.0} #{sample}  {Verdict(result.Passed),-7} {result.Seconds,6:F1} s");
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Summary: one row per prompt × model × temperature.
var summary = new StringBuilder();
summary.AppendLine("| Prompt | Model | Temp | Passed | Distinct answers | Avg output tokens | Avg time |");
summary.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

foreach (var group in results.GroupBy(r => (r.Prompt, r.Model, r.Temperature)))
{
    var checkedRuns = group.Where(r => r.Passed is not null).ToList();
    var passed = checkedRuns.Count == 0
        ? "read it"
        : $"{checkedRuns.Count(r => r.Passed == true)}/{checkedRuns.Count}";
    var distinct = group.Select(r => Regex.Replace(r.Answer, @"\s+", " ")).Distinct().Count();

    summary.AppendLine($"| {group.Key.Prompt} | {group.Key.Model} | {group.Key.Temperature:0.0} | {passed} | " +
                       $"{distinct} of {group.Count()} | {group.Average(r => r.OutputTokens):F0} | {group.Average(r => r.Seconds):F1} s |");
}

// Every answer, so free-text prompts can be read and compared.
var details = new StringBuilder();
foreach (var r in results)
{
    details.AppendLine($"### {r.Prompt} · {r.Model} · t={r.Temperature:0.0} · #{r.Sample} · {Verdict(r.Passed)}");
    details.AppendLine();
    details.AppendLine("```");
    details.AppendLine(r.Answer);
    details.AppendLine("```");
    details.AppendLine();
}

Directory.CreateDirectory("results");
var file = Path.Combine("results", $"kata-{only?.Replace(':', '-') ?? "all"}-{DateTime.Now:yyyyMMdd-HHmm}.md");
File.WriteAllText(file,
    $"# Prompt kata results, {DateTime.Now:yyyy-MM-dd HH:mm}\n\n## Summary\n\n{summary}\n## All answers\n\n{details}");

Console.WriteLine();
Console.WriteLine(summary);
Console.WriteLine($"Saved: {file}");

static string Verdict(bool? passed) => passed switch
{
    true => "PASS",
    false => "FAIL",
    null => "read it",
};