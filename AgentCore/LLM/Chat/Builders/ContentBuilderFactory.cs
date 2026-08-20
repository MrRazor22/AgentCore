using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

/// <summary>
/// Composite builder that dynamically dispatches incoming deltas to specialized modality builders,
/// managing multi-stream lifecycle, demuxing, and deterministic IsFinal completion.
/// </summary>
public sealed class ContentBuilder : IContentBuilder
{
    private class ActiveStream
    {
        public string? Id { get; set; }
        public int? Index { get; set; }
        public IContentBuilder Builder { get; init; } = null!;
    }

    private readonly List<ActiveStream> _activeStreams = new();

    public IEnumerable<IContent> Append(IContentDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        var targetType = GetBuilderType(delta);

        ActiveStream? stream = null;

        if (delta.Index.HasValue)
        {
            stream = _activeStreams.FirstOrDefault(s => s.Index == delta.Index.Value)
                  ?? _activeStreams.FirstOrDefault(s => !string.IsNullOrEmpty(delta.Id) && s.Id == delta.Id);
        }
        else if (!string.IsNullOrEmpty(delta.Id))
        {
            stream = _activeStreams.FirstOrDefault(s => s.Id == delta.Id);
        }
        else
        {
            var matching = _activeStreams.Where(s => s.Builder.GetType() == targetType).ToList();
            if (matching.Count > 1)
            {
                throw new InvalidOperationException($"Ambiguous delta: multiple active {delta.GetType().Name} streams exist.");
            }
            stream = matching.FirstOrDefault();
        }


        if (stream == null)
        {
            stream = new ActiveStream
            {
                Id = delta.Id,
                Index = delta.Index,
                Builder = CreateBuilder(delta)
            };
            _activeStreams.Add(stream);
        }
        else
        {
            if (string.IsNullOrEmpty(stream.Id) && !string.IsNullOrEmpty(delta.Id))
                stream.Id = delta.Id;
            if (!stream.Index.HasValue && delta.Index.HasValue)
                stream.Index = delta.Index;
        }

        foreach (var item in stream.Builder.Append(delta))
        {
            yield return item;
        }

        if (delta.IsFinal)
        {
            _activeStreams.Remove(stream);
        }
    }

    private static IContentBuilder CreateBuilder(IContentDelta delta) => delta switch
    {
        TextDelta => new TextContentBuilder(),
        ReasoningDelta => new ReasoningContentBuilder(),
        ToolCallDelta => new ToolCallContentBuilder(),
        _ => throw new NotSupportedException($"No content builder registered for delta type '{delta.GetType().FullName}'.")
    };

    // If Type is occasionally needed:
    private static Type GetBuilderType(IContentDelta delta) => CreateBuilder(delta).GetType();
}







