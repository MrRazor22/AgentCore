using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Runtime.CompilerServices;

namespace AgentCore.Example;

public sealed class StreamingLLMLayer : LLMLayer
{
    private readonly Action<ILLMOutput> _onEvent;

    public StreamingLLMLayer(Action<ILLMOutput> onEvent)
    {
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
    }

    public override async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in base.StreamAsync(messages, options, tools, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            try
            {
                _onEvent(evt);
            }
            catch
            {
                // Prevent callback exceptions from interrupting the stream
            }
            yield return evt;
        }
    }
}
