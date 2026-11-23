using Labs.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Phase02.ProviderSwitch.Tests;

public class ProviderConfigurationTests
{
    [Fact]
    public void Fake_provider_from_config_needs_no_model()
    {
        using var host = BuildHost(("Ai:Provider", "Fake"));

        var chat = host.Services.GetRequiredService<IChatClient>();

        Assert.NotNull(chat.GetService<FakeChatClient>());
    }

    [Fact]
    public void Unknown_provider_fails_at_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildHost(("Ai:Provider", "Gemini")));

        Assert.Contains("Ai:Provider", exception.Message);
    }

    private static IHost BuildHost(params (string Key, string Value)[] settings)
    {
        // No appsettings.json, no environment variables: only what the test passes in
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(
            settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));
        builder.AddLabAi();

        return builder.Build();
    }
}