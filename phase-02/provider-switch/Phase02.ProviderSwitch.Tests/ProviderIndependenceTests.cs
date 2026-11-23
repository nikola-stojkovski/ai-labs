using Labs.Common;
using Microsoft.Extensions.AI;
using Phase02.ProviderSwitch.Core;
using Xunit;

namespace Phase02.ProviderSwitch.Tests;

public class ProviderIndependenceTests
{
    // Assembly names of provider SDKs. Business code may reference none of them.
    private static readonly string[] ProviderAssemblyPrefixes =
    [
        "OllamaSharp",
        "OpenAI",
        "Microsoft.Extensions.AI.OpenAI",
        "Azure.AI.OpenAI",
        "Azure.AI.Inference",
    ];

    private static readonly PayslipAmounts Payslip = new(1000m, 100m, 280m, 620m);

    [Fact]
    public void Core_does_not_reference_any_provider_assembly()
    {
        var providerReferences = typeof(PayslipExplainer).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .Where(name => ProviderAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(providerReferences);
    }

    [Fact]
    public async Task Explainer_works_with_any_IChatClient()
    {
        const string fakeAnswer = "Gross pay 1000 minus income tax 100 and social contributions 280 gives net pay 620.";
        using var fake = new FakeChatClient(fakeAnswer);
        var explainer = new PayslipExplainer(fake);

        var result = await explainer.ExplainNetPayAsync(Payslip, "Why is my net pay lower?", TestContext.Current.CancellationToken);

        Assert.Equal(fakeAnswer, result.Answer);
        Assert.Equal("fake", result.ModelId);
    }

    [Fact]
    public async Task Template_travels_with_the_payslip_not_in_the_system_prompt()
    {
        using var fake = new FakeChatClient();
        var explainer = new PayslipExplainer(fake);

        await explainer.ExplainNetPayAsync(Payslip, "Why is my net pay lower?", TestContext.Current.CancellationToken);

        var system = Assert.Single(fake.LastMessages, message => message.Role == ChatRole.System);
        var user = Assert.Single(fake.LastMessages, message => message.Role == ChatRole.User);

        Assert.DoesNotContain("Answer in exactly this format", system.Text);
        Assert.Contains("Answer in exactly this format", user.Text);
        Assert.Contains("<payslip>", user.Text);
        Assert.Contains("Net: 620", user.Text);
    }

    [Fact]
    public async Task Explanations_use_temperature_zero()
    {
        using var fake = new FakeChatClient();
        var explainer = new PayslipExplainer(fake);

        await explainer.ExplainNetPayAsync(Payslip, "Why is my net pay lower?", TestContext.Current.CancellationToken);

        Assert.Equal(0f, fake.LastOptions?.Temperature);
    }
}