using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class TextContentBuilder : IContentBuilder
{
    public bool CanHandle(IContentDelta delta) => delta is TextDelta;

    public IEnumerable<IContent> Append(IContentDelta delta)
    {
        if (delta is TextDelta text && !string.IsNullOrEmpty(text.Value))
        {
            yield return new Text(text.Value);
        }
    }
}



