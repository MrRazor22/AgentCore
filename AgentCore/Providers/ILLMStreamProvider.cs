using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using AgentCore.Tokens;

namespace AgentCore.Providers;

public class LLMInitOptions
{
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
}

public interface ILLMStreamProvider
{
    IAsyncEnumerable<IContentDelta> StreamAsync(LLMRequest request, CancellationToken ct = default);

    FinishReason FinishReason { get; }

    TokenUsage? Usage { get; }
}
