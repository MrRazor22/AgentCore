using AgentCore.Conversation;
using AgentCore.Schema;
using AgentCore.Tools;

namespace AgentCore.LLM;

public sealed record LLMMetadata(
    string Id,
    int ContextWindow);

public interface ILLMProvider
{ 
  /// <summary>
  /// Gets metadata describing a model(s) supported by this provider.
  /// </summary> 
    Task<LLMMetadata> GetModelInfoAsync(string? modelName = null, CancellationToken ct = default);

    IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default);
}
