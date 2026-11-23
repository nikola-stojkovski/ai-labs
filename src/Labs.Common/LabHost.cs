using Microsoft.Extensions.Hosting;

namespace Labs.Common;

public static class LabHost
{
    public static IHost Create(string[] args, Action<IHostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.AddLabAi();
        configure?.Invoke(builder);

        return builder.Build();
    }
}
