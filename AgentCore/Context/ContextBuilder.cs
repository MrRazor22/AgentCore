using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public sealed class ContextBuilder
{
    internal Func<ILoggerFactory, IContext>? Factory { get; private set; }
    internal List<ContextLayer> Layers { get; } = [];

    public ContextBuilder Use(Func<ILoggerFactory, IContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
        return this;
    }

    public ContextBuilder AddLayer(ContextLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Layers.Add(layer);
        return this;
    }

    public ContextBuilder WithChatContext(
        int contextWindow = 50000,
        int? reserveTokens = null,
        int? maxSingleMessageTokens = null,
        ICompactor? compactor = null,
        ITokenizer? counter = null,
        ITruncator? truncator = null,
        Func<Role, string?, IMessageAssembler>? assemblerFactory = null)
    {
        return Use(lf => new ChatContext(
            contextWindow: contextWindow,
            reserveTokens: reserveTokens,
            maxSingleMessageTokens: maxSingleMessageTokens,
            compactor: compactor,
            counter: counter,
            truncator: truncator,
            assemblerFactory: assemblerFactory,
            logger: lf.CreateLogger<ChatContext>()
        ));
    }

    internal IContext Build(ILoggerFactory lf, ILLM baseLlm)
    {
        IContext context = Factory != null
            ? Factory(lf)
            : new ChatContext(compactor: new Summarizer(baseLlm), logger: lf.CreateLogger<ChatContext>());

        foreach (var layer in Layers)
        {
            layer.Attach(context);
            context = layer;
        }

        return context;
    }
}
