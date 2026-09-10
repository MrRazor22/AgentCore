using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public class AgentBuilder
{
    private ILogger<AgentBuilder> _logger = NullLogger<AgentBuilder>.Instance;
    private int _maxIterations = 20;
    private readonly List<IContent> _instructions = [];
    private Func<ILoggerFactory, ILLM>? _llmFactory;
    private Func<ILoggerFactory, ITooling>? _toolingFactory;
    private Func<ILoggerFactory, IContext>? _contextFactory;
    private readonly List<LLMLayer> _llmLayers = []; 
    private readonly List<ITool> _tools = [];
    private readonly List<ToolingLayer> _toolingLayers = [];
    private readonly List<ContextLayer> _contextLayers = []; 
    private ILoggerFactory? _loggerFactory; 

    public AgentBuilder WithMaxIterations(int maxIterations)
    {
        _maxIterations = maxIterations;
        return this;
    }

    public AgentBuilder WithInstructions(string prompt) => WithInstructions([new Text(prompt)]);

    public AgentBuilder WithInstructions(IEnumerable<IContent> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        foreach (var content in contents)
        {
            if (content != null) _instructions.Add(content);
        }
        return this;
    }

     
    public AgentBuilder AddLLMLayer(LLMLayer layer) { _llmLayers.Add(layer); return this; } 
    public AgentBuilder WithLLM(Func<ILoggerFactory, ILLM> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _llmFactory = factory;
        return this;
    }

    public AgentBuilder WithTools(params ITool[] tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            _tools.Add(tool);
        }
        return this;
    }
    public AgentBuilder WithTooling(Func<ILoggerFactory, ITooling> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _toolingFactory = factory;
        return this;
    }
    public AgentBuilder AddToolingLayer(ToolingLayer layer)
    {
        _toolingLayers.Add(layer); return this;
    }
    
    public AgentBuilder WithContext(Func<ILoggerFactory, IContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _contextFactory = factory;
        return this;
    } 
    public AgentBuilder AddContextLayer(ContextLayer layer) { _contextLayers.Add(layer); return this; }

    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
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

        var frozenInstructions = _instructions.Count > 0 ? _instructions.ToArray() : null;

        _logger.LogInformation("Agent built: Tools={ToolCount} Instructions={InstructionCount} Provider={ProviderType} Context={ContextType} LLMLayers={LLMLayers} ToolingLayers={ToolingLayers} ContextLayers={ContextLayers}",
            frozenTools.Length,
            frozenInstructions?.Length ?? 0,
            provider.GetType().Name,
            context.GetType().Name,
            _llmLayers.Count,
            _toolingLayers.Count,
            _contextLayers.Count);

        return new Agent(context, provider, tooling, frozenInstructions, _maxIterations);
    }
}
