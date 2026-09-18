using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public class AgentBuilder
{
    private ILogger<AgentBuilder> _logger = NullLogger<AgentBuilder>.Instance;
    private int _maxIterations = 20;
    private IReadOnlyList<IContent> _instructions = [new Text("You are a helpful AI assistant.")];
    private readonly LLMBuilder _llm = new();
    private readonly ToolBuilder _tooling = new();
    private readonly ContextBuilder _context = new();
    private ILoggerFactory? _loggerFactory;

    public AgentBuilder WithMaxIterations(int maxIterations)
    {
        _maxIterations = maxIterations;
        return this;
    } 

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

    public AgentBuilder UseToolbox(Action<ToolBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_tooling);
        return this;
    }

    public AgentBuilder UseContext(Action<ContextBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_context);
        return this;
    }

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
        var (tooling, tools) = _tooling.Build(lf);
        var context = _context.Build(lf, baseProvider);

        _logger.LogInformation("Agent built: Tools={ToolCount} Instructions={InstructionCount} Provider={ProviderType} Context={ContextType} LLMLayers={LLMLayers} ToolingLayers={ToolingLayers} ContextLayers={ContextLayers}",
            tools.Count,
            _instructions.Count,
            provider.GetType().Name,
            context.GetType().Name,
            _llm.Layers.Count,
            _tooling.Layers.Count,
            _context.Layers.Count);

        return new Agent(context, provider, tooling, tools, _instructions, _maxIterations);
    }
}
