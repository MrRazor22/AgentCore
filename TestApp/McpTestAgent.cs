using AgentCore;
using AgentCore.BuiltInTools;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.BuiltInTools;
using AgentCore.MCP.Server;
using AgentCore.Memory;
using AgentCore.Providers.Embeddings;
using AgentCore.Tokens;
using AgentCore.Providers.MEAI;
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
        var chatStore = new ChatFileStore(new() { PersistDir = @"D:\AgentCore\mcp-history" });
        
        // Semantic memory (IAgentMemory) - AMFS-style memory with confidence decay
        var memoryStore = new FileStore(@"D:\AgentCore\memory", "mcp");
        var loggerFactory = ConfigureLogging();
        
        // Embeddings provider for semantic search
        var embeddings = new OpenAIEmbeddings("your-openai-api-key", "text-embedding-3-small", 1536);

        var agent = LLMAgent.Create("mcp-agent")
            .WithMemory(chatStore)
            .WithMemory(new MemoryEngine(memoryStore, null!, embeddings, null, null, loggerFactory.CreateLogger<MemoryEngine>()))
            .WithInstructions("role", "You are a helpful AI assistant with access to various tools.")
            .AddOpenAI("model", "lmstudio", "http://127.0.0.1:1234/v1", new() { ContextLength = 8000 })
            
            .WithTools<GeoTools>()
            .WithTools<WeatherTool>()
            .WithTools<ConversionTools>()
            .WithTools<MathTools>()
            .WithTools<SearchTools>()

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