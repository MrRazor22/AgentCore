using System.Runtime.CompilerServices;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

/// <summary>
/// Composite builder that dynamically dispatches incoming deltas to specialized modality builders.
/// </summary>
public sealed class CompositeContentBuilder : IContentBuilder
{
    private IContentBuilder? _activeBuilder;

    public async IAsyncEnumerable<IContent> AppendAsync(IContentDelta delta, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delta);

        if (_activeBuilder == null || !MatchesActive(delta))
        {
            _activeBuilder = delta switch
            {
                TextDelta => new TextContentBuilder(),
                ReasoningDelta => new ReasoningContentBuilder(),
                ToolCallDelta => new ToolCallContentBuilder(),
                _ => throw new NotSupportedException($"No content builder registered for delta type '{delta.GetType().FullName}'.")
            };
        }

        await foreach (var item in _activeBuilder.AppendAsync(delta, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private bool MatchesActive(IContentDelta delta) =>
        _activeBuilder switch
        {
            TextContentBuilder => delta is TextDelta,
            ReasoningContentBuilder => delta is ReasoningDelta,
            ToolCallContentBuilder => delta is ToolCallDelta,
            _ => false
        };
}



