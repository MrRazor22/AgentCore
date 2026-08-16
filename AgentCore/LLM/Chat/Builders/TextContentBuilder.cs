using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class TextContentBuilder : IContentBuilder
{
    private readonly StringBuilder _builder = new();

    public bool TryAppend(IContentDelta delta)
    {
        if (delta is not TextDelta text) return false;
        if (!string.IsNullOrEmpty(text.Value))
            _builder.Append(text.Value);
        return true;
    }

    public IReadOnlyList<IContent> ToContents() =>
        _builder.Length == 0 ? [] : [new Text(_builder.ToString())];
}
