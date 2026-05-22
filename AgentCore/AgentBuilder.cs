using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using AgentCore.Context;
using AgentCore.Json;
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
    private ILLMExecutor? _customLlm;
    private IToolExecutor? _customTools;
    private IAgentRuntime? _agentRuntime;

    private readonly List<IMiddleware<LLMRequest, IAsyncEnumerable<LLMEvent>>> _llmMiddlewares = new();
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
    public AgentBuilder WithLLMExecutor(ILLMExecutor executor) { _customLlm = executor; return this; }
    public AgentBuilder WithToolExecutor(IToolExecutor executor) { _customTools = executor; return this; }

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

    public AgentBuilder WithResponseSchema<T>()
    {
        _providerOptions ??= new LLMOptions();
        _providerOptions.ResponseSchema = Json.JsonSchemaExtensions.GetSchemaFor<T>();
        return this;
    }

    public AgentBuilder WithResponseSchema(Type type)
    {
        _providerOptions ??= new LLMOptions();
        _providerOptions.ResponseSchema = type?.GetSchemaForType();
        return this;
    }




    public AgentBuilder UseLLMMiddleware(IMiddleware<LLMRequest, IAsyncEnumerable<LLMEvent>> middleware)
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

        if (_provider == null && _customLlm == null)
            throw new InvalidOperationException("No LLM provider or executor registered. Call WithProvider() or WithLLMExecutor().");

        var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
        var chatStore = _chatStore ?? new ChatFileStore("./chat-history");
        var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter();
        var contextManager = _contextManager ?? new ContextManager(tokenCounter, loggerFactory.CreateLogger<ContextManager>());
        var tokenManager = _tokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());
        var memory = _memory;

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);
        _logger.LogDebug("Tool registration: TotalTools={ToolCount}", registry.Tools.Count);

        // Pure executors — no middleware knowledge
        ILLMExecutor llm = _customLlm ?? new LLMExecutor(_provider!, tokenCounter, tokenManager, loggerFactory.CreateLogger<LLMExecutor>());
        IToolExecutor tools = _customTools ?? new ToolExecutor(registry, loggerFactory.CreateLogger<ToolExecutor>());

        // Builder owns middleware wiring via generic MiddlewarePipeline
        if (_llmMiddlewares.Count > 0)
        {
            var p = new MiddlewarePipeline<LLMRequest, IAsyncEnumerable<LLMEvent>>(
                (req, ct) => Task.FromResult(llm.StreamAsync(req, ct)));
            foreach (var mw in _llmMiddlewares) p.Use(mw);
            llm = new PipelineLLMExecutor(p);
        }
        if (_toolMiddlewares.Count > 0)
        {
            var p = new MiddlewarePipeline<ToolCall, ToolResult>(
                (call, ct) => tools.HandleToolCallAsync(call, ct));
            foreach (var mw in _toolMiddlewares) p.Use(mw);
            tools = new PipelineToolExecutor(p);
        }

        var baseOptions = _providerOptions ?? new LLMOptions();
        var agentRuntime = _agentRuntime ?? new AgentRuntime(
            contextManager, memory, baseOptions, registry.Tools,
            tokenCounter, _config, loggerFactory.CreateLogger<AgentRuntime>());

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount}", _config.Name, registry.Tools.Count);

        return new LLMAgent(
            chatStore, llm, tools, contextManager, memory, tokenCounter,
            agentRuntime, baseOptions, registry.Tools, _config,
            loggerFactory.CreateLogger<LLMAgent>());
    }

    private sealed class PipelineLLMExecutor(MiddlewarePipeline<LLMRequest, IAsyncEnumerable<LLMEvent>> pipeline) : ILLMExecutor
    {
        public IAsyncEnumerable<LLMEvent> StreamAsync(LLMRequest request, CancellationToken ct = default)
            => Unwrap(pipeline.InvokeAsyncWithTerminal(request, ct), ct);

        private static async IAsyncEnumerable<LLMEvent> Unwrap(
            Task<IAsyncEnumerable<LLMEvent>> task,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var e in (await task.ConfigureAwait(false)).WithCancellation(ct).ConfigureAwait(false))
                yield return e;
        }
    }

    private sealed class PipelineToolExecutor(MiddlewarePipeline<ToolCall, ToolResult> pipeline) : IToolExecutor
    {
        public Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
            => pipeline.InvokeAsyncWithTerminal(call, ct);
    }
}
