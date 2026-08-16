using System.Text;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ReasoningContentBuilder : IContentBuilder
{
    private readonly StringBuilder _sb = new();

    public bool CanAccept(IContentDelta delta) => delta is ReasoningDelta;

    public void Append(IContentDelta delta)
    {
        if (delta is ReasoningDelta rd && !string.IsNullOrEmpty(rd.Thought))
        {
            _sb.Append(rd.Thought);
        }
    }

    public IReadOnlyList<IContent> Build()
    {
        var thought = _sb.ToString();
        return string.IsNullOrEmpty(thought) ? Array.Empty<IContent>() : new IContent[] { new Reasoning(thought) };
    }
}
