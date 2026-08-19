using System.Runtime.CompilerServices;
using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class TextContentBuilder : IContentBuilder
{
    private class TextBlockState
    {
        public string Id { get; set; } = "";
        public int? Index { get; set; }
        public StringBuilder Buffer { get; } = new();
        public bool Emitted { get; set; }
    }

    private readonly List<TextBlockState> _blocks = new();

    public async IAsyncEnumerable<IContent> AppendAsync(IContentDelta contentDelta, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (contentDelta is not TextDelta delta) yield break;

        TextBlockState? state = null;

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
                    throw new InvalidOperationException("Ambiguous text delta: multiple active text blocks exist.");
                state = _blocks.Count == 1 ? _blocks[0] : null;
            }

            if (state == null)
            {
                state = new TextBlockState
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

        if (!string.IsNullOrEmpty(delta.Value))
        {
            state.Buffer.Append(delta.Value);
        }

        if (delta.IsFinal && !state.Emitted && state.Buffer.Length > 0)
        {
            state.Emitted = true;
            await Task.Yield();
            yield return new Text(state.Buffer.ToString());
        }
    }
}










