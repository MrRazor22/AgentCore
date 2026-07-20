using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.LLM;

public sealed class LLMCapabilities
{
    public int ContextWindow { get; set; } = 8000;
    public int ReservedTokens { get; set; } = 2000;
}

public sealed class LLMOptions
{
    public string? Model { get; init; }
    public float? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public JsonSchema? ResponseSchema { get; init; }
}

public interface ILLM
{
    LLMCapabilities GetCapabilities();

    IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default);
}
