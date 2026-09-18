using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;

namespace AgentCore.LLM;

public sealed class LLMBuilder
{
    internal Func<ILoggerFactory, ILLM>? Factory { get; private set; }
    internal List<LLMLayer> Layers { get; } = [];

    public LLMBuilder Use(Func<ILoggerFactory, ILLM> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
        return this;
    }

    public LLMBuilder Use(LLMDelegate middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        return AddLayer(new LLMLayer(middleware));
    }

    public LLMBuilder AddLayer(LLMLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Layers.Add(layer);
        return this;
    }

    internal (ILLM Base, ILLM Final) Build(ILoggerFactory lf)
    {
        if (Factory == null)
            throw new InvalidOperationException("No LLM provider registered. Call UseLLM() with a provider.");

        var baseProvider = Factory(lf);
        ILLM provider = baseProvider;
        foreach (var layer in Layers)
        {
            layer.Attach(provider);
            provider = layer;
        }

        return (baseProvider, provider);
    }
}
