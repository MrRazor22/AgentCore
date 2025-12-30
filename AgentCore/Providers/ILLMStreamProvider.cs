using AgentCore.LLM.Protocol;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace AgentCore.Providers
{
    public sealed class LLMInitOptions
    {
        public string? BaseUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
    }
    public interface ILLMStreamProvider
    {
        IAsyncEnumerable<LLMStreamChunk> StreamAsync<T>(
            LLMRequest<T> request,
            CancellationToken ct = default);
    }
}
