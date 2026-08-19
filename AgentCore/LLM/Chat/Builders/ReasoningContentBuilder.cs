using System.Runtime.CompilerServices;
using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ReasoningContentBuilder : IContentBuilder
{
    private class ReasoningBlockState
    {
        public string Id { get; set; } = "";
        public int? Index { get; set; }
        public StringBuilder Buffer { get; } = new();
        public bool Emitted { get; set; }
    }

    private readonly List<ReasoningBlockState> _blocks = new();

    public async IAsyncEnumerable<IContent> AppendAsync(IContentDelta contentDelta, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (contentDelta is not ReasoningDelta delta) yield break;

        ReasoningBlockState? state = null;

        if (delta.Index.HasValue)
        {
            state = _blocks.FirstOrDefault(b => b.Index == delta.Index.Value)
                 ?? _blocks.FirstOrDefault(b => b.Id == delta.Id && !string.IsNullOrEmpty(delta.Id));
        }
        else if (!string.IsNullOrEmpty(delta.Id))
        {
            state = _blocks.FirstOrDefault(b => b.Id == delta.Id);
        }

        if (state == null)
        {
            if (!delta.Index.HasValue && string.IsNullOrEmpty(delta.Id))
            {
                if (_blocks.Count > 1)
                    throw new InvalidOperationException("Ambiguous reasoning delta: multiple active reasoning blocks exist.");
                state = _blocks.Count == 1 ? _blocks[0] : null;
            }

            if (state == null)
            {
                state = new ReasoningBlockState
                {
                    Id = delta.Id ?? "",
                    Index = delta.Index
                };
                _blocks.Add(state);
            }
        }

        if (!string.IsNullOrEmpty(delta.Id) && state.Id != delta.Id)
        {
            state.Id = delta.Id;
        }

        if (!string.IsNullOrEmpty(delta.Thought))
        {
            state.Buffer.Append(delta.Thought);
        }

        if (delta.IsFinal && !state.Emitted && state.Buffer.Length > 0)
        {
            state.Emitted = true;
            await Task.Yield();
            yield return new Reasoning(state.Buffer.ToString());
        }
    }
}










