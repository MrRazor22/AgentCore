using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private IContextCompactor? _contextCompactor;
    private ITokenCounter? _tokenCounter;
    private ITokenManager? _tokenManager;
    private ILoggerFactory? _loggerFactory;
    private ILLMProvider? _provider;
    private LLMOptions? _providerOptions;
    private IEmbeddingProvider? _embeddingProvider;

    private readonly List<MemoryItem> _blocks = [];

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
    public AgentBuilder WithMemory(IMemoryStore store, MemoryEngineOptions? options = null)
    {
        if (_provider == null)
            throw new InvalidOperationException("Cannot create MemoryEngine without LLM provider. Call AddTornadoLLMProvider or WithProvider first.");
        if (_embeddingProvider == null)
            throw new InvalidOperationException("Cannot create MemoryEngine without embedding provider. Call AddTornadoEmbeddingProvider or WithEmbeddingProvider first.");
        
        _memory = new MemoryEngine(store, _provider, _embeddingProvider, options, _loggerFactory?.CreateLogger<MemoryEngine>());
        return this;
    }
    public AgentBuilder WithContextCompactor(IContextCompactor compactor) { _contextCompactor = compactor; return this; }

    /// <summary>Adds a memory item with instructions or working memory.</summary>
    public AgentBuilder WithInstructions(string label, string value, int limit = 0, Role role = Role.System, bool readOnly = true)
    {
        _blocks.Add(new MemoryItem(label, value, role, limit, readOnly));
        return this;
    } 

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

    public AgentBuilder WithEmbeddingProvider(IEmbeddingProvider provider)
    {
        _embeddingProvider = provider;
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
        var contextCompactor = _contextCompactor ?? new SummarizingContextCompactor(tokenCounter, loggerFactory.CreateLogger<SummarizingContextCompactor>(), _provider);
        var tokenManager = _tokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        var memory = _memory; // Memory is optional - if null, no semantic memory

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);

        _logger.LogDebug("Tool registration: TotalTools={ToolCount}", registry.Tools.Count);

        // Register scratchpad tools if memory items are provided
        if (_blocks.Count > 0)
            registry.RegisterAll(new ScratchpadTools(_blocks));

        var toolExecutor = new ToolExecutor(
            registry,
            loggerFactory.CreateLogger<ToolExecutor>());

        var llmExecutor = new LLMExecutor(
            _provider,
            registry,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>());

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount} MemoryItems={ItemCount} ProviderType={ProviderType}",
            _config.Name,
            registry.Tools.Count,
            _blocks.Count,
            _provider.GetType().Name);

        return new LLMAgent(
            chatStore,
            llmExecutor,
            toolExecutor,
            contextCompactor,
            memory,
            tokenCounter,
            _providerOptions ?? new LLMOptions(),
            _config,
            loggerFactory.CreateLogger<LLMAgent>());
    } 
}
