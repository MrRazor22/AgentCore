namespace AgentCore.LLM.Chat;

/// <summary>
/// Routes and accumulates streaming lifecycle events across multiple active concurrent streams,
/// yielding settled <see cref="IContent"/> items immediately as each individual stream ends.
/// </summary>
public sealed class ContentAssembler
{
    private readonly TextAccumulator _text = new();
    private readonly ReasoningAccumulator _reasoning = new();
    private readonly ToolCallAccumulator _tools = new();

    /// <summary>
    /// Ingests a streaming lifecycle event, routing by ID, and immediately returns completed <see cref="IContent"/> items.
    /// </summary>
    public IReadOnlyList<IContent> Receive(ILLMOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        switch (output)
        {
            case TextDelta d:         _text.Append(d.Text); return [];
            case TextEnd:             return _text.Complete();

            case ReasoningDelta d:    _reasoning.Append(d.Thought); return [];
            case ReasoningEnd:        return _reasoning.Complete();

            case ToolCallStart s:     _tools.Start(s.Id, s.Name, s.Index); return [];
            case ToolCallDelta d:     _tools.Append(d.Id, d.Arguments); return [];
            case ToolCallEnd e:       return _tools.Complete(e.Id);

            default:                  return [];
        }
    }
}
