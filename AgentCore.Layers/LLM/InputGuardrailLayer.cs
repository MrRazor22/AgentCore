using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using System.Runtime.CompilerServices;

namespace AgentCore.Layers.LLM;

public delegate ValueTask<IReadOnlyList<IContent>?> InputGuardrail(
    IReadOnlyList<Message> messages,
    CancellationToken ct);

public sealed class InputGuardrailLayer(InputGuardrail guardrail, ILLM? inner = null) : LLMLayer(inner)
{
    private readonly InputGuardrail _guardrail = guardrail ?? throw new ArgumentNullException(nameof(guardrail));

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var violation = await _guardrail(messages, ct).ConfigureAwait(false);
        if (violation is { Count: > 0 })
        {
            var id = Guid.NewGuid().ToString("N");
            yield return new MessageStart(Role.Assistant, Id: id);
            foreach (var content in violation)
                yield return new MessageDelta(id, Content: content);
            yield return new MessageEnd(Id: id);
            yield break;
        }

        await foreach (var evt in base.GenerateAsync(messages, tools, responseSchema, ct).WithCancellation(ct).ConfigureAwait(false))
            yield return evt;
    }
}
