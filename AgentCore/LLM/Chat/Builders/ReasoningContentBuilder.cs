using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ReasoningContentBuilder : IContentBuilder
{
    private readonly StringBuilder _buffer = new();

    public IEnumerable<IContent> Append(IContentDelta delta)
    {
        if (delta is ReasoningDelta reasoning && !string.IsNullOrEmpty(reasoning.Thought))
        {
            _buffer.Append(reasoning.Thought);
        }

        if (delta.IsFinal && _buffer.Length > 0)
        {
            yield return new Reasoning(_buffer.ToString());
            _buffer.Clear();
        }
    }
}












