using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace AgentCore.Diagnostics;

public class BackgroundTraceProcessor : BackgroundService
{
    private readonly ChannelReader<TraceSnapshot> _channelReader;
    private readonly TraceDispatcher _dispatcher;

    public BackgroundTraceProcessor(ChannelReader<TraceSnapshot> channelReader, TraceDispatcher dispatcher)
    {
        _channelReader = channelReader;
        _dispatcher = dispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var trace in _channelReader.ReadAllAsync(stoppingToken))
        {
            await _dispatcher.DispatchAsync(trace, stoppingToken);
        }
    }
}
