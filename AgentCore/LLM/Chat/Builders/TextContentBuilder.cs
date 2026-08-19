using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class TextContentBuilder : IContentBuilder
{
    private readonly StringBuilder _buffer = new();

    public IEnumerable<IContent> Append(IContentDelta delta)
    {
        if (delta is TextDelta text && !string.IsNullOrEmpty(text.Value))
        {
            _buffer.Append(text.Value);
        }

        if (delta.IsFinal && _buffer.Length > 0)
        {
            yield return new Text(_buffer.ToString());
            _buffer.Clear();
        }
    }
}












