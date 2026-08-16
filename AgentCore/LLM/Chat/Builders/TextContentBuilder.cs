using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class TextContentBuilder : IContentBuilder
{
    private readonly StringBuilder _sb = new();

    public bool CanAccept(IContentDelta delta) => delta is TextDelta;

    public void Append(IContentDelta delta)
    {
        if (delta is TextDelta td && !string.IsNullOrEmpty(td.Value))
        {
            _sb.Append(td.Value);
        }
    }

    public IReadOnlyList<IContent> Build()
    {
        var text = _sb.ToString();
        return string.IsNullOrEmpty(text) ? Array.Empty<IContent>() : new IContent[] { new Text(text) };
    }
}
