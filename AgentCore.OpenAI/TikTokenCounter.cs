using AgentCore.Tokens;
using SharpToken;

namespace AgentCore.Providers.OpenAI;

public sealed class TikTokenCounter(string encodingName) : ITokenCounter
{
    private readonly GptEncoding _encoding = GptEncoding.GetEncoding(encodingName);

    public int Count(string payload) => string.IsNullOrEmpty(payload) ? 0 : _encoding.Encode(payload).Count;
}
