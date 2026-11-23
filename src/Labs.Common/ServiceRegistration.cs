using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

namespace Labs.Common;

public static class ServiceRegistration
{
    public static IHostApplicationBuilder AddLabAi(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(LabAiOptions.SectionName).Get<LabAiOptions>()
                      ?? new LabAiOptions();

        switch (options.Provider)
        {
            case AiProvider.Ollama:
                AddOllama(builder.Services, options);
                break;
            case AiProvider.OpenAI:
                AddOpenAi(builder.Services, options);
                break;
            case AiProvider.Fake:
                builder.Services.AddChatClient(new FakeChatClient());
                break;
            default:
                throw new InvalidOperationException($"Unknown AI provider '{options.Provider}'.");
        }

        return builder;
    }


    private static void AddOllama(IServiceCollection services, LabAiOptions options)
    {
        // One HttpClient for both models; long timeout because Ollama runs on the CPU
        var http = new HttpClient
        {
            BaseAddress = new Uri(options.Endpoint),
            Timeout = TimeSpan.FromMinutes(10),
        };

        services.AddChatClient(new OllamaApiClient(http, options.ChatModel));
        services.AddEmbeddingGenerator<string, Embedding<float>>(new OllamaApiClient(http, options.EmbeddingModel));
    }

    private static void AddOpenAi(IServiceCollection services, LabAiOptions options)
    {
        // The OpenAI SDK has its own timeout (NetworkTimeout, 100 s by default), separate from HttpClient.Timeout.
        // Same lesson as the Aspire resilience handler in Phase 0: every layer has its own timeout.
        var client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(options.OpenAiEndpoint),
                NetworkTimeout = TimeSpan.FromMinutes(10),
            });

        services.AddChatClient(client.GetChatClient(options.ChatModel).AsIChatClient());
        services.AddEmbeddingGenerator(client.GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator());
    }
}
