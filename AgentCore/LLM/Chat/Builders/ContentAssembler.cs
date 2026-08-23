using System.Collections.Concurrent;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

/// <summary>
/// Routes and accumulates incoming <see cref="StreamChunk"/> items across multiple active concurrent streams
/// (e.g. interleaved text, parallel tool calls, reasoning, multimodal content) to specialized <see cref="IContentBuilder"/>s.
/// </summary>
public sealed class ContentAssembler : IContentAssembler
{
    private class ActiveStream
    {
        public string? Id { get; set; }
        public int? Index { get; set; }
        public IContentBuilder Builder { get; init; } = null!;
    }

    private static readonly ConcurrentDictionary<Type, Func<IContentBuilder>> DefaultRegistry = new()
    {
        [typeof(TextChunk)] = () => new TextContentBuilder(),
        [typeof(ReasoningChunk)] = () => new ReasoningContentBuilder(),
        [typeof(ToolCallChunk)] = () => new ToolCallContentBuilder()
    };

    private readonly Dictionary<Type, Func<IContentBuilder>> _customRegistry = new();
    private readonly List<ActiveStream> _activeStreams = new();

    /// <summary>
    /// Registers a custom builder factory for a specific <see cref="IContentChunk"/> type (e.g., ImageChunk, AudioChunk).
    /// </summary>
    public ContentAssembler RegisterBuilder<TChunk>(Func<IContentBuilder> factory) where TChunk : IContentChunk
    {
        ArgumentNullException.ThrowIfNull(factory);
        _customRegistry[typeof(TChunk)] = factory;
        return this;
    }

    /// <summary>
    /// Registers a global default builder factory for a specific <see cref="IContentChunk"/> type.
    /// </summary>
    public static void RegisterGlobalBuilder<TChunk>(Func<IContentBuilder> factory) where TChunk : IContentChunk
    {
        ArgumentNullException.ThrowIfNull(factory);
        DefaultRegistry[typeof(TChunk)] = factory;
    }

    /// <summary>
    /// Ingests a streaming chunk, routes to the appropriate modality builder, and yields completed or streaming <see cref="IContent"/> items.
    /// </summary>
    public IEnumerable<IContent> Receive(StreamChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var chunkType = chunk.Content.GetType();
        ActiveStream? stream = null;

        if (chunk.Index.HasValue)
        {
            stream = _activeStreams.FirstOrDefault(s => s.Index == chunk.Index.Value)
                  ?? _activeStreams.FirstOrDefault(s => !string.IsNullOrEmpty(chunk.Id) && s.Id == chunk.Id);
        }
        else if (!string.IsNullOrEmpty(chunk.Id))
        {
            stream = _activeStreams.FirstOrDefault(s => s.Id == chunk.Id);
        }
        else
        {
            var targetBuilderType = ResolveBuilder(chunk.Content).GetType();
            var matching = _activeStreams.Where(s => s.Builder.GetType() == targetBuilderType).ToList();
            if (matching.Count > 1)
            {
                throw new InvalidOperationException($"Ambiguous delta: multiple active {chunkType.Name} streams exist.");
            }
            stream = matching.FirstOrDefault();
        }

        if (stream == null)
        {
            stream = new ActiveStream
            {
                Id = chunk.Id,
                Index = chunk.Index,
                Builder = ResolveBuilder(chunk.Content)
            };
            _activeStreams.Add(stream);
        }
        else
        {
            if (string.IsNullOrEmpty(stream.Id) && !string.IsNullOrEmpty(chunk.Id))
                stream.Id = chunk.Id;
            if (!stream.Index.HasValue && chunk.Index.HasValue)
                stream.Index = chunk.Index;
        }

        foreach (var item in stream.Builder.Append(chunk))
        {
            yield return item;
        }

        if (chunk.IsFinal)
        {
            _activeStreams.Remove(stream);
        }
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="Receive(StreamChunk)"/>.
    /// </summary>
    public IEnumerable<IContent> Append(StreamChunk chunk) => Receive(chunk);

    private IContentBuilder ResolveBuilder(IContentChunk content)
    {
        var type = content.GetType();
        if (_customRegistry.TryGetValue(type, out var customFactory))
        {
            return customFactory();
        }

        if (DefaultRegistry.TryGetValue(type, out var defaultFactory))
        {
            return defaultFactory();
        }

        throw new NotSupportedException($"No content builder registered for chunk type '{type.FullName}'.");
    }
}
