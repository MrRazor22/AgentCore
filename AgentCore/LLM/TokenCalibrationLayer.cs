using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Runtime.CompilerServices;

namespace AgentCore.LLM;

internal sealed class TokenCalibrationLayer : LLMLayer
{
    private readonly ITokenCounter _tokenCounter;

    public TokenCalibrationLayer(ITokenCounter tokenCounter)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
    }

    public override async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var output in Inner.StreamAsync(messages, options, tools, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (output is Metadata metadata && metadata.InputTokens > 0)
            {
                _tokenCounter.ObserveActualCount(messages, tools, metadata.InputTokens);
            }
            yield return output;
        }
    }
}
