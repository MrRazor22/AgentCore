using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public static class ContentBuilderFactory
{
    public static IContentBuilder Create(IContentDelta delta) => delta switch
    {
        TextDelta => new TextContentBuilder(),
        ReasoningDelta => new ReasoningContentBuilder(),
        ToolCallDelta => new ToolCallContentBuilder(),
        _ => throw new NotSupportedException($"No content builder registered for delta type '{delta?.GetType().FullName}'.")
    };
}
