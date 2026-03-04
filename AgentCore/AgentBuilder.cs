using AgentCore.Chat;
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

    // Callback storage
    private Func<ToolCall, CancellationToken, Task<IContent?>>? _beforeToolCall;
    private Func<ToolCall, IContent?, CancellationToken, Task<IContent?>>? _afterToolCall;
    private Func<IReadOnlyList<Message>, LLMOptions, CancellationToken, Task<IReadOnlyList<LLMEvent>?>>? _beforeModelCall;
    private Func<IReadOnlyList<LLMEvent>, CancellationToken, Task>? _afterModelCall;

    public AgentBuilder() { }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithInstructions(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }

    public AgentBuilder WithMemory(IAgentMemory memory) { Memory = memory; return this; }
    public AgentBuilder WithContextManager(IContextManager contextManager) { ContextManager = contextManager; return this; }
    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { TokenCounter = tokenCounter; return this; }
    public AgentBuilder WithTokenManager(ITokenManager tokenManager) { TokenManager = tokenManager; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory) { LoggerFactory = loggerFactory; return this; }
    public AgentBuilder WithProvider(ILLMProvider provider) { Provider = provider; return this; }

    // Executor callbacks
    public AgentBuilder BeforeToolCall(Func<ToolCall, CancellationToken, Task<IContent?>> callback) { _beforeToolCall = callback; return this; }
    public AgentBuilder AfterToolCall(Func<ToolCall, IContent?, CancellationToken, Task<IContent?>> callback) { _afterToolCall = callback; return this; }
    public AgentBuilder BeforeModelCall(Func<IReadOnlyList<Message>, LLMOptions, CancellationToken, Task<IReadOnlyList<LLMEvent>?>> callback) { _beforeModelCall = callback; return this; }
    public AgentBuilder AfterModelCall(Func<IReadOnlyList<LLMEvent>, CancellationToken, Task> callback) { _afterModelCall = callback; return this; }

    public LLMAgent Build()
    {
        if (Provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call AddOpenAI().");

        var loggerFactory = LoggerFactory ?? NullLoggerFactory.Instance;
        var memory = Memory ?? new FileMemory();
        var tokenCounter = TokenCounter ?? new ApproximateTokenCounter();
        var contextManager = ContextManager ?? new ContextManager(tokenCounter, loggerFactory.CreateLogger<ContextManager>());
        var tokenManager = TokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);

        var toolExecutor = new ToolExecutor(registry)
        {
            BeforeCall = _beforeToolCall,
            AfterCall = _afterToolCall
        };

        var llmExecutor = new LLMExecutor(
            Provider,
            registry,
            contextManager,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>())
        {
            BeforeCall = _beforeModelCall,
            AfterCall = _afterModelCall
        };

        var executor = new ToolCallingLoop(
            memory,
            llmExecutor,
            toolExecutor,
            loggerFactory.CreateLogger<ToolCallingLoop>());

        return new LLMAgent(executor, memory, _config, loggerFactory.CreateLogger<LLMAgent>());
    }
}
