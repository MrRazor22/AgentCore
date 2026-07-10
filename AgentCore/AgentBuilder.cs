using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;

namespace AgentCore;

public sealed class AgentConfig
{
    public string Name { get; set; } = "agent";
    public IContent? Instructions { get; set; }
    public int? MaxToolCalls { get; set; } = null;
}

public sealed class AgentBuilder
{
    private readonly AgentConfig _config = new();
    private readonly List<Action<IToolRegistry>> _toolRegistrations = [];
    private ILogger<AgentBuilder> _logger;

    private IMemory? _memory;
    private ITooling? _tooling;
    private ILLM? _llm;
    private ITokenCounter? _tokenCounter;
    private ILoggerFactory? _loggerFactory;
    private ILLMProvider? _provider;
    private LLMOptions? _providerOptions;

    private readonly List<Func<IMemory, IMemory>> _memoryLayers = [];
    private readonly List<Func<ITooling, ITooling>> _toolingLayers = [];
    private readonly List<Func<ILLM, ILLM>> _llmLayers = [];

    public AgentBuilder()
    {
        _logger = NullLogger<AgentBuilder>.Instance;
    }

    public AgentConfig Config => _config;

    public ILoggerFactory LoggerFactory
    {
        get => _loggerFactory ??= NullLoggerFactory.Instance;
        set
        {
            _loggerFactory = value;
            _logger = value?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
        }
    }

    public ITokenCounter TokenCounter
    {
        get => _tokenCounter ??= new ApproximateTokenCounter();
        set => _tokenCounter = value;
    }

    public ILLMProvider? LlmProvider
    {
        get => _provider;
        set => _provider = value;
    }

    public LLMOptions ProviderOptions
    {
        get => _providerOptions ??= new LLMOptions();
        set => _providerOptions = value;
    }

    public IMemory Memory
    {
        get
        {
            if (_memory == null)
            {
                if (_provider == null)
                    throw new InvalidOperationException("No LLM provider registered. Call WithProvider() before retrieving Memory.");
                IMemory baseMemory = new ChatMemoryService(TokenCounter, _provider);
                foreach (var layer in _memoryLayers)
                {
                    baseMemory = layer(baseMemory);
                }
                _memory = baseMemory;
            }
            return _memory;
        }
        set => _memory = value;
    }

    public IToolRegistry ToolRegistry
    {
        get
        {
            if (_toolRegistry == null)
            {
                _toolRegistry = new ToolRegistry();
                foreach (var init in _toolRegistrations) init(_toolRegistry);
                _toolRegistrations.Clear();
            }
            return _toolRegistry;
        }
        set => _toolRegistry = value;
    }
    private IToolRegistry? _toolRegistry;

    public ITooling Tooling
    {
        get
        {
            if (_tooling == null)
            {
                ITooling baseTooling = new ToolingService(ToolRegistry, LoggerFactory.CreateLogger<ToolingService>());
                foreach (var layer in _toolingLayers)
                {
                    baseTooling = layer(baseTooling);
                }
                _tooling = baseTooling;
            }
            return _tooling;
        }
        set => _tooling = value;
    }

    public ILLM Llm
    {
        get
        {
            if (_llm == null)
            {
                if (LlmProvider == null)
                    throw new InvalidOperationException("No LLM provider registered. Call WithProvider() before retrieving Llm.");
                ILLM baseLlm = new LLMService(
                    LlmProvider,
                    ToolRegistry,
                    TokenCounter,
                    LoggerFactory.CreateLogger<LLMService>());
                foreach (var layer in _llmLayers)
                {
                    baseLlm = layer(baseLlm);
                }
                _llm = baseLlm;
            }
            return _llm;
        }
        set => _llm = value;
    }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithInstructions(string prompt) { _config.Instructions = new Text(prompt); return this; }
    public AgentBuilder WithTools(Action<IToolRegistry> configure) { _toolRegistrations.Add(configure); return this; }

    public AgentBuilder UseMemory(IMemory memory) { _memory = memory; return this; }
    public AgentBuilder WithMemory(IMemory memory) => UseMemory(memory);
    public AgentBuilder AddMemoryLayer(Func<IMemory, IMemory> layer) { _memoryLayers.Add(layer); return this; }

    public AgentBuilder UseTooling(ITooling tooling) { _tooling = tooling; return this; }
    public AgentBuilder AddToolingLayer(Func<ITooling, ITooling> layer) { _toolingLayers.Add(layer); return this; }

    public AgentBuilder UseLlm(ILLM llm) { _llm = llm; return this; }
    public AgentBuilder AddLlmLayer(Func<ILLM, ILLM> layer) { _llmLayers.Add(layer); return this; }

    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        LoggerFactory = loggerFactory;
        return this;
    }
    public AgentBuilder WithProvider(ILLMProvider provider, LLMOptions? options = null)
    {
        _provider = provider;
        if (options != null) _providerOptions = options;
        return this;
    }

    public IAgent Build()
    {
        _logger.LogInformation("Agent build started: Name={AgentName}", _config.Name);

        if (LlmProvider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call WithProvider().");

        var tokenCounter = TokenCounter;
        var tooling = Tooling;
        var llm = Llm;
        var memory = Memory;

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount} ProviderType={ProviderType}",
            _config.Name,
            ToolRegistry.Tools.Count,
            LlmProvider.GetType().Name);

        return new LLMAgent(
            llm,
            tooling,
            memory,
            tokenCounter,
            ProviderOptions,
            _config,
            LoggerFactory.CreateLogger<LLMAgent>());
    }

}
