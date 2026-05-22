using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using AgentCore.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentCore;

public sealed class AgentConfig
{
    public string Name { get; set; } = "agent";
    public string? SystemPrompt { get; set; }
    public int? MaxToolCalls { get; set; } = null;
}

public sealed class AgentBuilder
{
    private readonly AgentConfig _config = new();
    private readonly List<Action<ToolRegistry>> _toolRegistrations = [];
    private ILogger<AgentBuilder> _logger;

    private IChatMemory? _chatStore;
    private IAgentMemory? _memory;
    private IContextManager? _contextManager;
    private ITokenCounter? _tokenCounter;
    private ITokenManager? _tokenManager;
    private ILoggerFactory? _loggerFactory;
    private ILLMProvider? _provider;
    private LLMOptions? _providerOptions;
    private IAgentRuntime? _agentRuntime;

    private readonly List<IMiddleware<AgentRequestContext, AgentResponse>> _unaryAgentMiddlewares = new();
    private readonly List<IMiddleware<AgentRequestContext, IAsyncEnumerable<AgentEvent>>> _streamingAgentMiddlewares = new();
    private readonly List<IMiddleware<LLMCallContext, IAsyncEnumerable<LLMEvent>>> _llmMiddlewares = new();
    private readonly List<IMiddleware<ToolCall, ToolResult>> _toolMiddlewares = new();

    public AgentBuilder()
    {
        _logger = NullLogger<AgentBuilder>.Instance;
    }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithSystemPrompt(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }
    public AgentBuilder WithTools(Action<ToolRegistry> configure) { _toolRegistrations.Add(configure); return this; }

    public AgentBuilder WithChatHistory(IChatMemory chatStore) { _chatStore = chatStore; return this; }
    public AgentBuilder WithMemory(IAgentMemory memory) { _memory = memory; return this; }
    public AgentBuilder WithContextManager(IContextManager contextManager) { _contextManager = contextManager; return this; }
    public AgentBuilder WithRuntime(IAgentRuntime agentRuntime) { _agentRuntime = agentRuntime; return this; }

    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
    public AgentBuilder WithTokenManager(ITokenManager tokenManager) { _tokenManager = tokenManager; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
        return this;
    }
    public AgentBuilder WithProvider(ILLMProvider provider, LLMOptions? options = null)
    {
        _provider = provider;
        if (options != null) _providerOptions = options;
        return this;
    }

    public AgentBuilder UseAgentMiddleware(IMiddleware<AgentRequestContext, AgentResponse> middleware)
    {
        if (middleware != null) _unaryAgentMiddlewares.Add(middleware);
        return this;
    }

    public AgentBuilder UseAgentMiddleware(IMiddleware<AgentRequestContext, IAsyncEnumerable<AgentEvent>> middleware)
    {
        if (middleware != null) _streamingAgentMiddlewares.Add(middleware);
        return this;
    }

    public AgentBuilder UseLLMMiddleware(IMiddleware<LLMCallContext, IAsyncEnumerable<LLMEvent>> middleware)
    {
        if (middleware != null) _llmMiddlewares.Add(middleware);
        return this;
    }

    public AgentBuilder UseToolMiddleware(IMiddleware<ToolCall, ToolResult> middleware)
    {
        if (middleware != null) _toolMiddlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Registers another agent as a tool. The LLM can call the sub-agent by name,
    /// passing a task string and receiving the agent's response.
    /// This enables multi-agent orchestration with zero infrastructure.
    /// </summary>
    public AgentBuilder WithAgentTool(IAgent agent, string name, string description)
    {
        _toolRegistrations.Add(registry =>
        {
            var inputSchema = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "object",
                ["properties"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["task"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The task or question to delegate to this agent"
                    }
                },
                ["required"] = new System.Text.Json.Nodes.JsonArray("task")
            };

            registry.Register(new Tooling.Tool
            {
                Name = name,
                Description = description,
                ParametersSchema = inputSchema,
                Invoker = async (args) =>
                {
                    var task = args.Length > 0 ? args[0]?.ToString() ?? "" : "";
                    var response = await agent.InvokeAsync(new Conversation.Text(task));
                    return new Conversation.Text(response.Text ?? "");
                }
            });
        });
        return this;
    }

    public LLMAgent Build()
    {
        _logger.LogInformation("Agent build started: Name={AgentName}", _config.Name);

        if (_provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call WithProvider().");

        var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
        var chatStore = _chatStore ?? new ChatFileStore("./chat-history");
        var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter();
        var contextManager = _contextManager ?? new ContextManager(tokenCounter, loggerFactory.CreateLogger<ContextManager>());
        var tokenManager = _tokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        var memory = _memory; // Memory is optional - if null, no semantic memory

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);

        _logger.LogDebug("Tool registration: TotalTools={ToolCount}", registry.Tools.Count);

        var toolExecutor = new ToolExecutor(
            registry,
            loggerFactory.CreateLogger<ToolExecutor>());

        var llmExecutor = new LLMExecutor(
            _provider,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>());

        // 1. Wrap LLM and Tool executors with middleware pipelines using decorator pattern
        var wrappedLlm = new MiddlewareLLMExecutor(llmExecutor, _llmMiddlewares);
        var wrappedTool = new MiddlewareToolExecutor(toolExecutor, _toolMiddlewares);

        // 2. Build the AgentRuntime loop orchestrator
        var baseOptions = _providerOptions ?? new LLMOptions();
        var agentRuntime = _agentRuntime ?? new AgentRuntime(
            contextManager,
            memory,
            baseOptions,
            tokenCounter,
            _config,
            loggerFactory.CreateLogger<AgentRuntime>());

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount} ProviderType={ProviderType}",
            _config.Name,
            registry.Tools.Count,
            _provider.GetType().Name);

        return new LLMAgent(
            chatStore,
            wrappedLlm,
            wrappedTool,
            contextManager,
            memory,
            tokenCounter,
            agentRuntime,
            baseOptions,
            registry.Tools,
            _config,
            loggerFactory.CreateLogger<LLMAgent>(),
            _unaryAgentMiddlewares,
            _streamingAgentMiddlewares);
    } 
}
