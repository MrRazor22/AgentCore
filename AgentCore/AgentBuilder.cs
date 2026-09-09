using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public class AgentBuilder
{
    private readonly List<Tool> _tools = [];
    private ILogger<AgentBuilder> _logger = NullLogger<AgentBuilder>.Instance;
    private IContent? _instructions;

    private Func<ILoggerFactory, ILLM>? _llmFactory;
    private Func<ILoggerFactory, IContext>? _contextFactory;
    private Func<ILoggerFactory, ITooling>? _toolingFactory;
    private int _maxIterations = 20;
    private ILoggerFactory? _loggerFactory;

    private readonly List<ToolingLayer> _toolingLayers = [];
    private readonly List<LLMLayer> _llmLayers = [];
    private readonly List<ContextLayer> _contextLayers = [];

    public AgentBuilder WithInstructions(string prompt) { _instructions = new Text(prompt); return this; }

    public AgentBuilder WithTools(params Tool[] tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            _tools.Add(tool);
        }
        return this;
    }

    public AgentBuilder WithContext(Func<ILoggerFactory, IContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _contextFactory = factory;
        return this;
    }

    public AgentBuilder AddContextLayer(ContextLayer layer) { _contextLayers.Add(layer); return this; }

    public AgentBuilder WithTooling(Func<ILoggerFactory, ITooling> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _toolingFactory = factory;
        return this;
    }

    public AgentBuilder AddToolingLayer(ToolingLayer layer) { _toolingLayers.Add(layer); return this; }

    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
        return this;
    }

    public AgentBuilder WithLLM(Func<ILoggerFactory, ILLM> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _llmFactory = factory;
        return this;
    }

    public AgentBuilder AddLLMLayer(LLMLayer layer) { _llmLayers.Add(layer); return this; }

    public AgentBuilder WithMaxIterations(int maxIterations)
    {
        _maxIterations = maxIterations;
        return this;
    }

    public Agent Build()
    {
        var lf = _loggerFactory ?? NullLoggerFactory.Instance;

        if (_llmFactory == null)
            throw new InvalidOperationException("No LLM provider registered. Call WithLLM().");

        var baseProvider = _llmFactory(lf);

        ILLM provider = baseProvider;
        foreach (var layer in _llmLayers)
        {
            layer.Attach(provider);
            provider = layer;
        }

        var frozenTools = _tools.ToArray();

        ITooling tooling = _toolingFactory != null
            ? _toolingFactory(lf)
            : new Tooling(frozenTools, lf.CreateLogger<Tooling>());
        foreach (var layer in _toolingLayers)
        {
            layer.Attach(tooling);
            tooling = layer;
        }

        IContext context = _contextFactory != null
            ? _contextFactory(lf)
            : new ChatContext(compactor: new Summarizer(baseProvider), logger: lf.CreateLogger<ChatContext>());

        foreach (var layer in _contextLayers)
        {
            layer.Attach(context);
            context = layer;
        }

        if (_instructions != null)
        {
            context.AppendAsync([new Message(Role.System, [_instructions])]).GetAwaiter().GetResult();
        }

        _logger.LogInformation("Agent built: Tools={ToolCount} Provider={ProviderType} Context={ContextType} LLMLayers={LLMLayers} ToolingLayers={ToolingLayers} ContextLayers={ContextLayers}",
            frozenTools.Length,
            provider.GetType().Name,
            context.GetType().Name,
            _llmLayers.Count,
            _toolingLayers.Count,
            _contextLayers.Count);

        return new Agent(context, provider, tooling, _maxIterations);
    }
}
