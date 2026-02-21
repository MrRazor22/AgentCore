using AgentCore.LLM.Protocol;

namespace AgentCore.Providers;

public sealed class LLMInitOptions
{
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
}

public interface ILLMStreamProvider
{
    IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken ct = default);
}
