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
    public ToolOptions ToolOptions { get; set; } = new();
}

public sealed class AgentBuilder
{
    private readonly AgentConfig _config = new();
    private readonly List<Action<ToolRegistry>> _toolRegistrations = [];
    private ILogger<AgentBuilder> _logger;

    private IChat? _chatStore;
    private IAgentMemory? _memory;
    private IContextCompactor? _contextCompactor;
    private ITokenCounter? _tokenCounter;
    private ITokenManager? _tokenManager;
    private ILoggerFactory? _loggerFactory;
    private ILLMProvider? _provider;
    private LLMOptions? _providerOptions;

    private readonly List<CoreMemoryBlock> _blocks = [];

    public AgentBuilder()
    {
        _logger = NullLogger<AgentBuilder>.Instance;
    }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithSystemPrompt(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }
    public AgentBuilder WithTools(Action<ToolRegistry> configure) { _toolRegistrations.Add(configure); return this; }
    public AgentBuilder WithToolOptions(Action<ToolOptions> configure) { configure(_config.ToolOptions); return this; }

    public AgentBuilder WithChatHistory(IChat chatStore) { _chatStore = chatStore; return this; }
    public AgentBuilder WithMemory(IAgentMemory memory) { _memory = memory; return this; }
    public AgentBuilder WithContextCompactor(IContextCompactor compactor) { _contextCompactor = compactor; return this; }

    /// <summary>Adds a core memory block with instructions or working memory.</summary>
    public AgentBuilder WithInstructions(string label, string value, int limit = 0, Role role = Role.System, bool readOnly = true)
    {
        _blocks.Add(new CoreMemoryBlock(label, value, role, limit, readOnly));
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

    public LLMAgent Build()
    {
        _logger.LogInformation("Agent build started: Name={AgentName}", _config.Name);

        if (_provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call WithProvider().");

        var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
        var chatStore = _chatStore ?? new ChatFileStore(new() { PersistDir = "./chat-history" });
        var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter();
        var contextCompactor = _contextCompactor ?? new SummarizingContextCompactor(tokenCounter, loggerFactory.CreateLogger<SummarizingContextCompactor>(), _provider);
        var tokenManager = _tokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        var memory = _memory ?? new CoreMemory();
        var coreMemory = memory as CoreMemory;
        
        // Add builder blocks to CoreMemory if it's the default/simple implementation
        if (coreMemory != null)
        {
            var blocksToUse = new List<CoreMemoryBlock>();

            if (_config.SystemPrompt != null)
                blocksToUse.Add(new CoreMemoryBlock("system", _config.SystemPrompt, Role.System, 0, readOnly: true));

            blocksToUse.AddRange(_blocks);

            _logger.LogDebug("Memory configuration: SystemPromptLength={PromptLength} BuilderBlocks={BlockCount}",
                _config.SystemPrompt?.Length ?? 0, _blocks.Count);

            // Reconstruct CoreMemory with all blocks
            memory = new CoreMemory(blocksToUse);
        }

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);

        _logger.LogDebug("Tool registration: TotalTools={ToolCount}", registry.Tools.Count);

        // Register scratchpad tools automatically if using CoreMemory
        if (memory is CoreMemory cm)
            registry.RegisterAll(new ScratchpadTools(cm.GetBlocks()));

        var toolExecutor = new ToolExecutor(
            registry,
            _config.ToolOptions,
            loggerFactory.CreateLogger<ToolExecutor>());

        var llmExecutor = new LLMExecutor(
            _provider,
            registry,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>());

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount} MemoryBlocks={BlockCount} ProviderType={ProviderType}",
            _config.Name,
            registry.Tools.Count,
            memory is CoreMemory coreMem ? coreMem.GetBlocks().Count : 0,
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
