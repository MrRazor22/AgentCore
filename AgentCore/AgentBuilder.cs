using AgentCore.Conversation;
using AgentCore.Schema;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public sealed class AgentBuilder
{
    private readonly ToolRegistry _registry = new();
    private ILogger<AgentBuilder> _logger;
    private IContent? _instructions;

    private IMemoryService? _memory;
    private IToolService? _tooling;
    private ILLMService? _llm;
    private ITokenCounter? _tokenCounter;
    private ILoggerFactory? _loggerFactory;
    private ILLMProvider? _provider;

    private readonly List<Func<IMemoryService, IMemoryService>> _memoryLayers = [];
    private readonly List<Func<IToolService, IToolService>> _toolingLayers = [];
    private readonly List<Func<ILLMService, ILLMService>> _llmLayers = [];

    public AgentBuilder()
    {
        _logger = NullLogger<AgentBuilder>.Instance;
    }

    public AgentServices? Services { get; private set; }

    public AgentBuilder WithInstructions(string prompt) { _instructions = new Text(prompt); return this; }

    public AgentBuilder WithTools<T>()
    {
        foreach (var tool in MethodTool.FromType(typeof(T)))
            _registry.Add(tool);
        return this;
    }

    public AgentBuilder WithTools(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        foreach (var tool in MethodTool.FromType(instance.GetType(), instance))
            _registry.Add(tool);
        return this;
    }

    public AgentBuilder UseMemory(IMemoryService memory) { _memory = memory; return this; } 
    public AgentBuilder AddMemoryLayer(Func<IMemoryService, IMemoryService> layer) { _memoryLayers.Add(layer); return this; }

    public AgentBuilder UseTooling(IToolService tooling) { _tooling = tooling; return this; }
    public AgentBuilder AddToolingLayer(Func<IToolService, IToolService> layer) { _toolingLayers.Add(layer); return this; }

    public AgentBuilder UseLLM(ILLMService llm) { _llm = llm; return this; }
    public AgentBuilder AddLLMLayer(Func<ILLMService, ILLMService> layer) { _llmLayers.Add(layer); return this; }

    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
        return this;
    }
    public AgentBuilder WithProvider(ILLMProvider provider) { _provider = provider; return this; }

    public IAgent Build()
    {
        var lf = _loggerFactory ?? NullLoggerFactory.Instance;
        return Build(services =>
            new Agent(
                services,
                _instructions,
                new ReActExecutor(services),
                lf.CreateLogger<Agent>()));
    }

    public IAgent Build(Func<AgentServices, IAgent> factory)
    {
        _logger.LogInformation("Agent build started");

        if (_provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call WithProvider().");

        var lf = _loggerFactory ?? NullLoggerFactory.Instance;
        var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter(logger: lf.CreateLogger<ApproximateTokenCounter>());

        IMemoryService memory = _memory ?? new ChatMemoryService(tokenCounter, _provider);
        foreach (var layer in _memoryLayers)
            memory = layer(memory);

        _logger.LogDebug("Tool registration: TotalTools={ToolCount}", _registry.Tools.Count);

        IToolService tooling = _tooling ?? new ToolService(_registry, lf.CreateLogger<ToolService>());
        foreach (var layer in _toolingLayers)
            tooling = layer(tooling);

        ILLMService llm = _llm ?? new LLMService(_provider, _registry, tokenCounter, logger: lf.CreateLogger<LLMService>());
        foreach (var layer in _llmLayers)
            llm = layer(llm);

        _logger.LogInformation("Agent build completed: Tools={ToolCount} ProviderType={ProviderType}",
            _registry.Tools.Count,
            _provider.GetType().Name);

        var services = new AgentServices(llm, tooling, memory);
        Services = services;

        return factory(services);
    }
}
