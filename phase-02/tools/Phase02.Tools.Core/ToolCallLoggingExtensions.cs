using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Phase02.Tools.Core;

public static class ToolCallLoggingExtensions
{
    /// <summary>
    /// UseFunctionInvocation with a low round-trip limit and a log line for every tool call.
    /// </summary>
    public static ChatClientBuilder UseLoggedFunctionInvocation(this ChatClientBuilder builder, int maximumIterations = 3) =>
        builder.Use((innerClient, services) =>
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Phase02.Tools");

            return new FunctionInvokingChatClient(innerClient, loggerFactory, services)
            {
                // The default is 40 round trips; at ~5 s each on CPU that's minutes for one confused question
                MaximumIterationsPerRequest = maximumIterations,

                FunctionInvoker = async (context, cancellationToken) =>
                {
                    // Arguments come from the model: log them like any untrusted input
                    var arguments = JsonSerializer.Serialize(
                        context.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value),
                        AIJsonUtilities.DefaultOptions);

                    logger.LogInformation(
                        "Round {Round}, call {Call}/{Count}: {Tool}({Arguments})",
                        context.Iteration + 1,
                        context.FunctionCallIndex + 1,
                        context.FunctionCount,
                        context.Function.Name,
                        arguments);

                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        var result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);

                        // Lab only: results contain pay data. In PayPilot, log the size, not the content.
                        logger.LogInformation(
                            "{Tool} returned in {Elapsed} ms: {Result}",
                            context.Function.Name,
                            stopwatch.ElapsedMilliseconds,
                            JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions));

                        return result;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "{Tool} failed after {Elapsed} ms", context.Function.Name, stopwatch.ElapsedMilliseconds);
                        throw;
                    }
                },
            };
        });
}