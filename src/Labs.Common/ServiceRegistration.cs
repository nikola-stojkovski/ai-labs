using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;

namespace Labs.Common;

public static class ServiceRegistration
{
    public static IHostApplicationBuilder AddLabAi(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(LabAiOptions.SectionName).Get<LabAiOptions>()
                      ?? new LabAiOptions();

        // One HttpClient for both models; long timeout because Ollama runs on the CPU
        var http = new HttpClient
        {
            BaseAddress = new Uri(options.Endpoint),
            Timeout = TimeSpan.FromMinutes(10),
        };

        builder.Services.AddChatClient(new OllamaApiClient(http, options.ChatModel));

        builder.Services.AddEmbeddingGenerator<string, Embedding<float>>(
            new OllamaApiClient(http, options.EmbeddingModel));

        return builder;
    }
}
