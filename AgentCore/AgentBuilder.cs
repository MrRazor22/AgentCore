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

    public IChat? ChatStore { get; set; }
    public IAgentMemory? Memory { get; set; }
    public IContextCompactor? ContextCompactor { get; set; }
    public ITokenCounter? TokenCounter { get; set; }
    public ITokenManager? TokenManager { get; set; }
    public ILoggerFactory? LoggerFactory { get; set; }
    public ILLMProvider? Provider { get; set; }
    public IApprovalService? ApprovalService { get; set; }
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

    public AgentBuilder WithMemory(IChat chatStore) { ChatStore = chatStore; return this; }
    public AgentBuilder WithMemory(IAgentMemory memory) { Memory = memory; return this; }
    public AgentBuilder WithContextCompactor(IContextCompactor compactor) { ContextCompactor = compactor; return this; }

    /// <summary>Adds read-only instructions (rules, persona) as a core memory block.</summary>
    public AgentBuilder WithInstructions(string label, string value, int limit = 0, Role role = Role.System)
    {
        _blocks.Add(new CoreMemoryBlock(label, value, role, limit, readOnly: true));
        return this;
    }

    /// <summary>Adds a writable scratchpad for agent working memory.</summary>
    public AgentBuilder WithScratchpad(string label, int limit = 2000)
    {
        _blocks.Add(new CoreMemoryBlock(label, "", Role.System, limit, readOnly: false));
        return this;
    }

    /// <summary>Loads instructions from a file as a read-only core memory block.</summary>
    public AgentBuilder WithInstructionFile(string path, int limit = 8000, string? label = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Instruction file not found.", path);
        label ??= Path.GetFileNameWithoutExtension(path);
        var content = File.ReadAllText(path);
        return WithInstructions(label, content, limit);
    } 

    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { TokenCounter = tokenCounter; return this; }
    public AgentBuilder WithTokenManager(ITokenManager tokenManager) { TokenManager = tokenManager; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        LoggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
        return this;
    }
    public AgentBuilder WithApprovalService(IApprovalService approvalService) { ApprovalService = approvalService; return this; }
    public AgentBuilder WithProvider(ILLMProvider provider, LLMOptions? options = null)
    {
        Provider = provider;
        if (options != null) _providerOptions = options;
        return this;
    }

    public LLMAgent Build()
    {
        _logger.LogInformation("Agent build started: Name={AgentName}", _config.Name);

        if (Provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call AddOpenAI().");

        var loggerFactory = LoggerFactory ?? NullLoggerFactory.Instance;
        var chatStore = ChatStore ?? new InMemoryChat();
        var tokenCounter = TokenCounter ?? new ApproximateTokenCounter();
        var contextCompactor = ContextCompactor ?? new SummarizingContextCompactor(tokenCounter, loggerFactory.CreateLogger<SummarizingContextCompactor>(), Provider);
        var tokenManager = TokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        _logger.LogDebug("Agent configuration: MemoryType={MemoryType} ContextCompactorType={CompactorType} TokenCounterType={TokenCounterType}",
            Memory?.GetType().Name ?? "CoreMemory",
            contextCompactor.GetType().Name,
            tokenCounter.GetType().Name);

        // Use provided memory or default CoreMemory
        var memory = Memory ?? new CoreMemory();
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
            loggerFactory.CreateLogger<ToolExecutor>(),
            ApprovalService);

        var llmExecutor = new LLMExecutor(
            Provider,
            registry,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>());

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount} MemoryBlocks={BlockCount} ProviderType={ProviderType}",
            _config.Name,
            registry.Tools.Count,
            memory is CoreMemory coreMem ? coreMem.GetBlocks().Count : 0,
            Provider?.GetType().Name ?? "Unknown");

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
