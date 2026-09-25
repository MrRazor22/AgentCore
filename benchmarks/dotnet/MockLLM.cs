using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.Benchmarks;

public sealed class MockLLM : ILLM
{
    private int _callCount;

    public async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
    {
        await Task.Yield();
        if (_callCount++ % 2 == 0)
        {
            yield return new MessageDelta(Content: new ToolCallStart(0, "call_1", "get_weather"));
            yield return new MessageDelta(Content: new ToolCallDelta(0, "{\"city\":\"London\"}"));
            yield return new MessageDelta(Content: new ToolCallEnd(0));
        }
        else
        {
            yield return new MessageDelta(Content: new TextDelta(0, "The weather in London is 15C and sunny."));
        }
    }
}
