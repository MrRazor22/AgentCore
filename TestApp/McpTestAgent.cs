using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.MCP.Server;
using AgentCore.Memory;
using AgentCore.Providers.Tornado;
using AgentCore.Tokens;
using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Chat.Models;
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

        // Conversation history (IChat) - stores chat messages per session
        var chatStore = new ChatFileStore(@"D:\AgentCore\mcp-history");
        
        var loggerFactory = ConfigureLogging();
        
        var api = new TornadoApi(new Uri("http://localhost"), "dummy");
        var modelName = "model";

        // Use RollingWindowMemory as default - conversation summarization + rolling window
        // For advanced semantic memory, implement a custom IAgentMemory
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new RollingWindowMemory(
            new TornadoLLMProvider(api, new ChatModel(modelName)),
            tokenCounter,
            new RollingWindowMemoryOptions { WindowSize = 10, EnableSummarization = true },
            loggerFactory.CreateLogger<RollingWindowMemory>());

        var builder = LLMAgent.Create("mcp-agent")
            .WithChatHistory(chatStore)
            .WithMemory(memory)
            .AddTornado(api, modelName, new LLMOptions { ContextLength = 8000 })
            
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithTools<SearchTools>()

            .WithLoggerFactory(loggerFactory);

        var agent = builder.Build();

        Console.WriteLine("Agent configured with tools: Geo, Weather, Conversion, Math, Search");
        Console.WriteLine("\nStarting MCP server (stdio transport)...\n");

        await AgentMcpServer.RunAsync(agent, new AgentMcpServerOptions
        {
            Name = "mcp-agent",
            Description = "A helpful AI assistant available via MCP protocol",
            Transport = McpTransportType.Stdio
        });
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