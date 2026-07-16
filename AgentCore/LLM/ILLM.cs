using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.LLM;

public sealed class LLMCapabilities
{
    public int ContextWindow { get; set; } = 2000;
    public int ReservedTokens { get; set; } = 2048;
}

public interface ILLM
{
    LLMCapabilities GetCapabilities();

    IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default);
}
