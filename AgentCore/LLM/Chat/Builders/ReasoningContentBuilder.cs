using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ReasoningContentBuilder : IContentBuilder
{
    public bool CanHandle(IContentDelta delta) => delta is ReasoningDelta;

    public IEnumerable<IContent> Append(IContentDelta delta)
    {
        if (delta is ReasoningDelta reasoning && !string.IsNullOrEmpty(reasoning.Thought))
        {
            yield return new Reasoning(reasoning.Thought);
        }
    }
}



