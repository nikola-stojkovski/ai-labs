using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Phase02.PiiRedaction.Core;

public static class PiiRedactionBuilderExtensions
{
    /// <summary>Adds PII redaction at this point in the pipeline. The logger comes from DI.</summary>
    public static ChatClientBuilder UsePiiRedaction(this ChatClientBuilder builder) =>
        builder.Use((innerClient, services) =>
            new PiiRedactingChatClient(innerClient, services.GetRequiredService<ILogger<PiiRedactingChatClient>>()));
}