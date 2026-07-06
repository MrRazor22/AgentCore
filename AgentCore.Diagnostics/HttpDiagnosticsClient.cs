using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AgentCore.Diagnostics;

public sealed class HttpDiagnosticsClient : ITraceObserver, ITraceExporter, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUri;
    private readonly Channel<IDiagnosticEvent> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;

    public HttpDiagnosticsClient(string baseUri)
    {
        _httpClient = new HttpClient();
        _baseUri = baseUri.TrimEnd('/');
        _channel = Channel.CreateUnbounded<IDiagnosticEvent>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        _cts = new CancellationTokenSource();
        _processingTask = Task.Run(ProcessEventsAsync);
    }

    public ValueTask OnTraceStartedAsync(TraceSnapshot trace)
    {
        _channel.Writer.TryWrite(new TraceStartedEvent(trace, DateTime.UtcNow));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSpanStartedAsync(SpanSnapshot span)
    {
        _channel.Writer.TryWrite(new SpanStartedEvent(span, DateTime.UtcNow));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSpanFinishedAsync(SpanSnapshot span)
    {
        _channel.Writer.TryWrite(new SpanFinishedEvent(span, DateTime.UtcNow));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTraceFinishedAsync(TraceSnapshot trace)
    {
        _channel.Writer.TryWrite(new TraceFinishedEvent(trace, DateTime.UtcNow));
        return ValueTask.CompletedTask;
    }

    public async Task ExportAsync(TraceSnapshot trace, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUri}/api/v1/traces";
            await _httpClient.PostAsJsonAsync(url, trace, ct);
        }
        catch
        {
            // Ignore exporter errors to avoid breaking the pipeline
        }
    }

    private async Task ProcessEventsAsync()
    {
        var token = _cts.Token;
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(token))
            {
                try
                {
                    string endpointName = evt switch
                    {
                        TraceStartedEvent => "trace-started",
                        SpanStartedEvent => "span-started",
                        SpanFinishedEvent => "span-finished",
                        TraceFinishedEvent => "trace-finished",
                        _ => throw new InvalidOperationException()
                    };

                    var url = $"{_baseUri}/api/v1/events/{endpointName}";
                    var content = JsonContent.Create(evt, evt.GetType());
                    await _httpClient.PostAsync(url, content, token);
                }
                catch
                {
                    // Ignore transient network errors
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
        try
        {
            _processingTask.GetAwaiter().GetResult();
        }
        catch { }
        _cts.Dispose();
        _httpClient.Dispose();
    }
}
