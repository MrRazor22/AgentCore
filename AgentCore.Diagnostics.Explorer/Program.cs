using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCore.Diagnostics;

var port = 5151;
string? storePath = null;
var openBrowser = false;

// Parse command line arguments
for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--port" or "-p" && i < args.Length - 1)
    {
        if (int.TryParse(args[i + 1], out var parsedPort)) port = parsedPort;
    }
    else if (args[i] is "--store" or "-s" && i < args.Length - 1)
    {
        storePath = args[i + 1];
    }
    else if (args[i] is "--open")
    {
        openBrowser = true;
    }
}

Console.WriteLine("AgentCore Diagnostics Explorer");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // Keep console quiet

// Register Dependencies
builder.Services.AddSingleton<TraceEventHub>();

if (!string.IsNullOrEmpty(storePath))
{
    storePath = Path.GetFullPath(storePath);
    Directory.CreateDirectory(storePath);
    Console.WriteLine($"Store: JsonTraceStore ({storePath})");
    builder.Services.AddSingleton<ITraceStore>(new JsonTraceStore(storePath));
}
else
{
    Console.WriteLine("Store: MemoryTraceStore (In-Memory)");
    builder.Services.AddSingleton<ITraceStore>(new MemoryTraceStore());
}

builder.Services.AddSingleton<ITraceReceiver, DefaultTraceReceiver>();

var app = builder.Build();

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    Converters = { new JsonStringEnumConverter() }
};

// ── Ingestion Endpoints (ITraceReceiver) ───────────────────────────────────

app.MapPost("/api/v1/events/trace-started", async (TraceStartedEvent evt, ITraceReceiver receiver) =>
{
    await receiver.ReceiveEventAsync(evt);
    return Results.Accepted();
});

app.MapPost("/api/v1/events/span-started", async (SpanStartedEvent evt, ITraceReceiver receiver) =>
{
    await receiver.ReceiveEventAsync(evt);
    return Results.Accepted();
});

app.MapPost("/api/v1/events/span-finished", async (SpanFinishedEvent evt, ITraceReceiver receiver) =>
{
    await receiver.ReceiveEventAsync(evt);
    return Results.Accepted();
});

app.MapPost("/api/v1/events/trace-finished", async (TraceFinishedEvent evt, ITraceReceiver receiver) =>
{
    await receiver.ReceiveEventAsync(evt);
    return Results.Accepted();
});

app.MapPost("/api/v1/traces", async (TraceSnapshot trace, ITraceReceiver receiver) =>
{
    await receiver.ReceiveTraceAsync(trace);
    return Results.Accepted();
});

// ── Query Endpoints (ITraceStore) ──────────────────────────────────────────

app.MapGet("/api/v1/traces", async (ITraceStore store) =>
{
    var traces = await store.QueryAsync(new TraceQuery());
    return Results.Json(traces, jsonOpts);
});

app.MapGet("/api/v1/traces/{id}", async (string id, ITraceStore store) =>
{
    var trace = await store.GetAsync(id);
    return trace is null ? Results.NotFound() : Results.Json(trace, jsonOpts);
});

// ── Streaming (SSE) ────────────────────────────────────────────────────────

app.MapGet("/api/v1/stream", async (HttpContext context, TraceEventHub hub) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");

    var clientId = Guid.NewGuid().ToString("N");
    var tcs = new TaskCompletionSource();

    context.RequestAborted.Register(() =>
    {
        hub.Unsubscribe(clientId);
        tcs.TrySetResult();
    });

    hub.Subscribe(clientId, async (evt) =>
    {
        try
        {
            var json = JsonSerializer.Serialize(evt, evt.GetType(), jsonOpts);
            await context.Response.WriteAsync($"data: {json}\n\n");
            await context.Response.Body.FlushAsync();
        }
        catch
        {
            hub.Unsubscribe(clientId);
            tcs.TrySetResult();
        }
    });

    Console.WriteLine($"[SSE] Client connected: {clientId}");
    await tcs.Task;
    Console.WriteLine($"[SSE] Client disconnected: {clientId}");
});

// ── UI fallback ────────────────────────────────────────────────────────────

app.MapGet("/", () => Results.Content(ExplorerUi.Html, "text/html"));
app.MapGet("/{**path}", () => Results.Content(ExplorerUi.Html, "text/html"));

app.Urls.Add($"http://localhost:{port}");

Console.WriteLine($"Explorer running at: http://localhost:{port}");
Console.WriteLine("Press Ctrl+C to stop.\n");

if (openBrowser)
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = $"http://localhost:{port}",
            UseShellExecute = true
        });
    }
    catch { /* ignore */ }
}

await app.RunAsync();
