using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public class AgentBuilder
{
    private ILogger<AgentBuilder> _logger = NullLogger<AgentBuilder>.Instance;
    private int _maxIterations = 20;
    private IReadOnlyList<IContent>? _instructions;
    private readonly LLMBuilder _llm = new();
    private readonly ToolingBuilder _tooling = new();
    private readonly ContextBuilder _context = new();
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
        _instructions = contents.Where(c => c != null).ToArray();
        return this;
    }

    public AgentBuilder UseLLM(Action<LLMBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_llm);
        return this;
    }

    public AgentBuilder WithLLM(Func<ILoggerFactory, ILLM> factory) => UseLLM(l => l.Use(factory));
    public AgentBuilder AddLLMLayer(LLMLayer layer) => UseLLM(l => l.AddLayer(layer));

    public AgentBuilder UseTooling(Action<ToolingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_tooling);
        return this;
    }

    public AgentBuilder WithTools(params ITool[] tools) => UseTooling(t => t.WithTools(tools));
    public AgentBuilder WithTools<T>() => UseTooling(t => t.WithTools<T>());
    public AgentBuilder WithTools(object instance) => UseTooling(t => t.WithTools(instance));
    public AgentBuilder AddToolingLayer(ToolingLayer layer) => UseTooling(t => t.AddLayer(layer));

    public AgentBuilder UseContext(Action<ContextBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_context);
        return this;
    }

    public AgentBuilder WithContext(Func<ILoggerFactory, IContext> factory) => UseContext(c => c.Use(factory));
    public AgentBuilder AddContextLayer(ContextLayer layer) => UseContext(c => c.AddLayer(layer));

    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
        return this;
    }

    public Agent Build()
    {
        var lf = _loggerFactory ?? NullLoggerFactory.Instance;

        var (baseProvider, provider) = _llm.Build(lf);
        var tooling = _tooling.Build(lf);
        var context = _context.Build(lf, baseProvider);

        _logger.LogInformation("Agent built: Tools={ToolCount} Instructions={InstructionCount} Provider={ProviderType} Context={ContextType} LLMLayers={LLMLayers} ToolingLayers={ToolingLayers} ContextLayers={ContextLayers}",
            _tooling.Tools.Count,
            _instructions?.Count ?? 0,
            provider.GetType().Name,
            context.GetType().Name,
            _llm.Layers.Count,
            _tooling.Layers.Count,
            _context.Layers.Count);

        return new Agent(context, provider, tooling, _instructions, _maxIterations);
    }
}
