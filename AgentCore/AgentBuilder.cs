using AgentCore.Chat;
using AgentCore.Execution;
using AgentCore.LLM;
using AgentCore.Runtime;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public sealed class AgentConfig
{
    public string Name { get; set; } = "agent";
    public string? SystemPrompt { get; set; }
    public int MaxIterations { get; set; } = 50;
    public ToolOptions ToolOptions { get; set; } = new();
}

public sealed class AgentBuilder
{
    private readonly AgentConfig _config = new();
    private readonly List<Action<ToolRegistry>> _toolRegistrations = [];

    public IAgentMemory? Memory { get; set; }
    public IContextManager? ContextManager { get; set; }
    public ITokenCounter? TokenCounter { get; set; }
    public ITokenManager? TokenManager { get; set; }
    public ILoggerFactory? LoggerFactory { get; set; }
    public ILLMProvider? Provider { get; set; }
    private LLMOptions? _providerOptions;

    // Pipeline storage
    private readonly List<PipelineMiddleware<ToolCall, Task<ToolResult>>> _toolMiddlewares = [];
    private readonly List<PipelineMiddleware<LLMCall, IAsyncEnumerable<LLMEvent>>> _llmMiddlewares = [];
    private readonly List<PipelineMiddleware<IAgentContext, IAsyncEnumerable<string>>> _agentMiddlewares = [];

    public AgentBuilder() { }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithInstructions(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }
    public AgentBuilder WithToolOptions(Action<ToolOptions> configure) { configure(_config.ToolOptions); return this; }

    public AgentBuilder WithMemory(IAgentMemory memory) { Memory = memory; return this; }
    public AgentBuilder WithContextManager(IContextManager contextManager) { ContextManager = contextManager; return this; }
    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { TokenCounter = tokenCounter; return this; }
    public AgentBuilder WithTokenManager(ITokenManager tokenManager) { TokenManager = tokenManager; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory) { LoggerFactory = loggerFactory; return this; }
    public AgentBuilder WithProvider(ILLMProvider provider, LLMOptions? options = null) 
    { 
        Provider = provider; 
        if (options != null) _providerOptions = options;
        return this; 
    }

    // Executor pipelines
    public AgentBuilder UseToolMiddleware(PipelineMiddleware<ToolCall, Task<ToolResult>> middleware) { _toolMiddlewares.Add(middleware); return this; }
    public AgentBuilder UseLLMMiddleware(PipelineMiddleware<LLMCall, IAsyncEnumerable<LLMEvent>> middleware) { _llmMiddlewares.Add(middleware); return this; }
    public AgentBuilder UseAgentMiddleware(PipelineMiddleware<IAgentContext, IAsyncEnumerable<string>> middleware) { _agentMiddlewares.Add(middleware); return this; }

    public LLMAgent Build()
    {
        if (Provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call AddOpenAI().");

        var loggerFactory = LoggerFactory ?? NullLoggerFactory.Instance;
        var memory = Memory ?? new InMemoryMemory();
        var tokenCounter = TokenCounter ?? new ApproximateTokenCounter();
        var contextManager = ContextManager ?? new SummarizingContextManager(tokenCounter, loggerFactory.CreateLogger<SummarizingContextManager>(), Provider);
        var tokenManager = TokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);

        var toolExecutor = new ToolExecutor(
            registry, 
            _config.ToolOptions, 
            _toolMiddlewares);

        var llmExecutor = new LLMExecutor(
            Provider,
            registry,
            contextManager,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>(),
            _llmMiddlewares);
        var executor = new ToolCallingLoop(
            memory,
            llmExecutor,
            toolExecutor,
            _providerOptions ?? new LLMOptions(),
            loggerFactory.CreateLogger<ToolCallingLoop>(),
            _agentMiddlewares);

        return new LLMAgent(executor, memory, _config, loggerFactory.CreateLogger<LLMAgent>());
    }
}
