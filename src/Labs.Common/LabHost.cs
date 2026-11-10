using Microsoft.Extensions.Hosting;

namespace Labs.Common;

public static class LabHost
{
    public static IHost Create(string[] args, Action<IHostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddLabAi();
        configure?.Invoke(builder);

        return builder.Build();
    }
}
