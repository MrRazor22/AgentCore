using System;
using System.Threading.Channels;
using AgentCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentDiagnostics(this IServiceCollection services, Action<TracerOptions> configure)
    {
        var options = new TracerOptions();
        configure(options);
        
        services.AddSingleton(options);
        
        var channel = Channel.CreateUnbounded<TraceSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        
        services.AddSingleton<ChannelWriter<TraceSnapshot>>(channel.Writer);
        services.AddSingleton<ChannelReader<TraceSnapshot>>(channel.Reader);
        
        services.AddSingleton<TraceDispatcher>(sp => new TraceDispatcher(options.Exporters));
        services.AddSingleton<IAgentTracer, Tracer>();
        
        services.AddHostedService<BackgroundTraceProcessor>();
        
        foreach (var exporter in options.Exporters)
        {
            if (exporter is ITraceStore store)
            {
                services.AddSingleton(store);
            }
        }
        
        return services;
    }
}
