using System.Runtime.CompilerServices;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCoreT10;

public record StreamObservedEvent(string EventType, object? Payload, DateTimeOffset Timestamp);

public sealed class StreamObservationLayer(
    Action<StreamObservedEvent>? onEvent = null,
    ILLM? inner = null) : LLMLayer(inner)
{
    private readonly Action<StreamObservedEvent>? _onEvent = onEvent;

    public List<StreamObservedEvent> ObservedEvents { get; } = [];
    public List<string> TokenDeltas { get; } = [];
    public bool Completed { get; private set; }

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Completed = false;

        await foreach (var evt in Inner.GenerateAsync(messages, tools, responseSchema, ct).ConfigureAwait(false))
        {
            RecordEvent(evt);
            yield return evt;
        }

        Completed = true;
        var compEvt = new StreamObservedEvent("completion", null, DateTimeOffset.UtcNow);
        ObservedEvents.Add(compEvt);
        _onEvent?.Invoke(compEvt);
    }

    private void RecordEvent(IMessageEvent evt)
    {
        switch (evt)
        {
            case MessageDelta { Content: TextDelta td }:
                TokenDeltas.Add(td.Text);
                var textEvt = new StreamObservedEvent("token_delta", td.Text, DateTimeOffset.UtcNow);
                ObservedEvents.Add(textEvt);
                _onEvent?.Invoke(textEvt);
                break;

            case MessageDelta { Content: ToolCall tc }:
                var tcEvt = new StreamObservedEvent("tool_call_delta", tc, DateTimeOffset.UtcNow);
                ObservedEvents.Add(tcEvt);
                _onEvent?.Invoke(tcEvt);
                break;

            case MessageDelta { Content: ToolCallDelta tcd }:
                var tcdEvt = new StreamObservedEvent("tool_call_delta", tcd, DateTimeOffset.UtcNow);
                ObservedEvents.Add(tcdEvt);
                _onEvent?.Invoke(tcdEvt);
                break;

            case MessageDelta { Content: Text t }:
                TokenDeltas.Add(t.Value);
                var fullTextEvt = new StreamObservedEvent("token_delta", t.Value, DateTimeOffset.UtcNow);
                ObservedEvents.Add(fullTextEvt);
                _onEvent?.Invoke(fullTextEvt);
                break;

            case MessageStart ms:
                var startEvt = new StreamObservedEvent("message_start", ms, DateTimeOffset.UtcNow);
                ObservedEvents.Add(startEvt);
                _onEvent?.Invoke(startEvt);
                break;

            case MessageEnd me:
                var endEvt = new StreamObservedEvent("message_end", me, DateTimeOffset.UtcNow);
                ObservedEvents.Add(endEvt);
                _onEvent?.Invoke(endEvt);
                break;
        }
    }
}
