using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ReasoningContentBuilder : IContentBuilder
{
    private readonly StringBuilder _builder = new();

    public bool TryAppend(IContentDelta delta)
    {
        if (delta is not ReasoningDelta reasoning) return false;
        if (!string.IsNullOrEmpty(reasoning.Thought))
            _builder.Append(reasoning.Thought);
        return true;
    }

    public IReadOnlyList<IContent> ToContents() =>
        _builder.Length == 0 ? [] : [new Reasoning(_builder.ToString())];
}
