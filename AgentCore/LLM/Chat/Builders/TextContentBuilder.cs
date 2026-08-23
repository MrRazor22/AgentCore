using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class TextContentBuilder : IContentBuilder
{
    private readonly StringBuilder _buffer = new();

    public IEnumerable<IContent> Append(StreamChunk chunk)
    {
        if (chunk.Content is TextChunk text && !string.IsNullOrEmpty(text.Text))
        {
            _buffer.Append(text.Text);
        }

        if (chunk.IsFinal && _buffer.Length > 0)
        {
            yield return new Text(_buffer.ToString());
            _buffer.Clear();
        }
    }
}












