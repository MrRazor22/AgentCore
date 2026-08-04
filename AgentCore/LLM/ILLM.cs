using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.LLM;

public sealed class LLMOptions
{
    public string? Model { get; init; }
    public float? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public JsonSchema? ResponseSchema { get; init; }
}

public interface ILLM
{
    IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default);
}
