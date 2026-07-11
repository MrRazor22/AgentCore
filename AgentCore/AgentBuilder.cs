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
    public ILoggerFactory? LoggerFactory => _loggerFactory;
    public ITokenCounter? TokenCounter => _tokenCounter;
    public ILLMProvider? LlmProvider => _provider;
    public LLMOptions? ProviderOptions => _providerOptions;
    public IMemory? Memory => _memory;
    public ITooling? Tooling => _tooling;
    public ILLM? Llm => _llm;

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

    public IAgent Build()
    {
        _logger.LogInformation("Agent build started: Name={AgentName}", _config.Name);

        if (_provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call WithProvider().");

        var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter(logger: _loggerFactory?.CreateLogger<ApproximateTokenCounter>());
        
        IMemory memory = _memory ?? new ChatMemoryService(tokenCounter, _provider);
        foreach (var layer in _memoryLayers)
        {
            memory = layer(memory);
        }

        var registry = new ToolRegistry(logger: _loggerFactory?.CreateLogger<ToolRegistry>());
        foreach (var init in _toolRegistrations) init(registry);

        _logger.LogDebug("Tool registration: TotalTools={ToolCount}", registry.Tools.Count);

        ITooling tooling = _tooling ?? new ToolingService(
            registry,
            _loggerFactory?.CreateLogger<ToolingService>());
        foreach (var layer in _toolingLayers)
        {
            tooling = layer(tooling);
        }

        ILLM llm = _llm ?? new LLMService(
            _provider,
            registry,
            tokenCounter,
            _loggerFactory?.CreateLogger<LLMService>());
        foreach (var layer in _llmLayers)
        {
            llm = layer(llm);
        }

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount} ProviderType={ProviderType}",
            _config.Name,
            registry.Tools.Count,
            _provider.GetType().Name);

        return new LLMAgent(
            llm,
            tooling,
            memory,
            tokenCounter,
            _providerOptions ?? new LLMOptions(),
            _config,
            _loggerFactory?.CreateLogger<LLMAgent>());
    }

}
