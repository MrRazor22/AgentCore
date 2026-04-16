using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.MCP.Server;
using AgentCore.Tokens;
using AgentCore.Providers.MEAI;
using AgentCore.Runtime;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace TestApp;

public static class McpTestAgent
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("=== MCP Server Demo ===");
        Console.WriteLine("This demo wraps an agent with an MCP server for stdio transport.\n");
        Console.WriteLine("The agent can be connected to from any MCP-compatible client.\n");

        var memory = new FileMemory(new() { PersistDir = @"D:\AgentCore\mcp-history" });

        var agent = LLMAgent.Create("mcp-agent")
            .WithMemory(memory)
            .WithInstructions("role", "You are a helpful AI assistant with access to various tools.")
            .AddOpenAI("model", "lmstudio", "http://127.0.0.1:1234/v1", new() { ContextLength = 8000 })
            
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithTools<SearchTools>()

            .UseToolMiddleware(MonitorToolExecution)
            .UseLLMMiddleware(MonitorLLMStream)
            .WithLoggerFactory(ConfigureLogging())
            .Build();

        Console.WriteLine("Agent configured with tools: Geo, Weather, Conversion, Math, Search");
        Console.WriteLine("\nStarting MCP server (stdio transport)...\n");

        await AgentMcpServer.RunAsync(agent, new AgentMcpServerOptions
        {
            Name = "mcp-agent",
            Description = "A helpful AI assistant available via MCP protocol",
            Transport = McpTransportType.Stdio
        });
    }

    private static async Task<ToolResult> MonitorToolExecution(
        ToolCall call,
        AgentCore.Execution.PipelineHandler<ToolCall, Task<ToolResult>> next,
        CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n🔧 Tool: {call.Name}({call.Arguments.ToJsonString()})");
        Console.ResetColor();

        var result = await next(call, ct);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Result: {result?.Result?.ForLlm()}");
        Console.ResetColor();

        return result!;
    }

    private static async IAsyncEnumerable<LLMEvent> MonitorLLMStream(
        LLMCall req,
        AgentCore.Execution.PipelineHandler<LLMCall, IAsyncEnumerable<LLMEvent>> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var events = new List<LLMEvent>();
        await foreach (var evt in next(req, ct).ConfigureAwait(false))
        {
            events.Add(evt);
            yield return evt;
        }

        var tools = events.OfType<ToolCallEvent>().Count();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(tools > 0
            ? $"\n📊 {events.Count} events, {tools} tool calls"
            : $"\n📊 {events.Count} events (complete)");
        Console.ResetColor();
    }

    private static ILoggerFactory ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(LogEventLevel.Information)
            .WriteTo.File(@"D:\AgentCore\McpAgent.log", LogEventLevel.Verbose, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        return LoggerFactory.Create(b => b.AddSerilog(Log.Logger));
    }
}