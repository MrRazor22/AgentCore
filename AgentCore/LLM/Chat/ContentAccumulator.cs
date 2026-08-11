using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

internal static class ContentAccumulator
{ 
    internal static async IAsyncEnumerable<IAgentResponse> AccumulateStream(
        this IAsyncEnumerable<ILLMOutput> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var textBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var toolCalls = new List<ToolCallDelta>();
        var emittedCount = 0;

        IEnumerable<IAgentResponse> FlushAccumulated(Type nextType)
        {
            if (nextType != typeof(TextDelta) && textBuilder.Length > 0)
            {
                var text = textBuilder.ToString();
                textBuilder.Clear();
                yield return new Text(text);
            }
            if (nextType != typeof(ReasoningDelta) && reasoningBuilder.Length > 0)
            {
                var thought = reasoningBuilder.ToString();
                reasoningBuilder.Clear();
                yield return new Reasoning(thought);
            }
            if (nextType != typeof(ToolCallDelta))
            {
                while (emittedCount < toolCalls.Count)
                {
                    var tcState = toolCalls[emittedCount++];
                    var name = tcState.Name.ToString().Trim();
                    if (string.IsNullOrEmpty(name)) continue;

                    var argsStr = tcState.Args.ToString().Trim();
                    JsonObject? parsedArgs = null;
                    if (!string.IsNullOrEmpty(argsStr))
                    {
                        try { parsedArgs = JsonNode.Parse(argsStr)?.AsObject(); } catch { }
                    }
                    yield return new ToolCall(tcState.AccumulatedId, name, parsedArgs ?? []);
                }
            }
        }

        ExceptionDispatchInfo? edi = null;
        ConfiguredCancelableAsyncEnumerable<ILLMOutput>.Enumerator enumerator = default;

        try
        {
            enumerator = stream.WithCancellation(cancellationToken).GetAsyncEnumerator();
            while (true)
            {
                ILLMOutput item;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    item = enumerator.Current;
                }
                catch (Exception ex)
                {
                    edi = ExceptionDispatchInfo.Capture(ex);
                    break;
                }

                switch (item)
                {
                    case TextDelta td:
                        foreach (var flushed in FlushAccumulated(typeof(TextDelta))) yield return flushed;
                        textBuilder.Append(td.Value);
                        break;

                    case ReasoningDelta rd:
                        foreach (var flushed in FlushAccumulated(typeof(ReasoningDelta))) yield return flushed;
                        reasoningBuilder.Append(rd.Thought);
                        break;

                    case ToolCallDelta tcd:
                        foreach (var flushed in FlushAccumulated(typeof(ToolCallDelta))) yield return flushed;

                        ToolCallDelta? state = null;
                        if (tcd.Index.HasValue)
                        {
                            state = toolCalls.FirstOrDefault(tc => tc.Index == tcd.Index.Value)
                                    ?? toolCalls.FirstOrDefault(tc => tc.AccumulatedId == tcd.Id && !string.IsNullOrEmpty(tcd.Id));
                            if (state == null)
                            {
                                state = tcd;
                                toolCalls.Add(state);
                            }
                        }
                        else if (!string.IsNullOrEmpty(tcd.Id))
                        {
                            state = toolCalls.FirstOrDefault(tc => tc.AccumulatedId == tcd.Id);
                            if (state == null)
                            {
                                state = tcd;
                                toolCalls.Add(state);
                            }
                        }

                        if (state == null)
                        {
                            if (toolCalls.Count == 1) state = toolCalls[0];
                            else if (toolCalls.Count > 1) throw new InvalidOperationException("Ambiguous tool call delta: multiple active tool calls exist.");
                            else
                            {
                                state = tcd;
                                toolCalls.Add(state);
                            }
                        }

                        if (!string.IsNullOrEmpty(tcd.Id) && state.AccumulatedId != tcd.Id)
                        {
                            state.AccumulatedId = tcd.Id;
                        }

                        if (!string.IsNullOrEmpty(tcd.NameDelta))
                        {
                            var cur = state.Name.ToString();
                            if (string.IsNullOrEmpty(cur) || (cur != tcd.NameDelta && !cur.EndsWith(tcd.NameDelta)))
                                state.Name.Append(tcd.NameDelta);
                        }
                        if (!string.IsNullOrEmpty(tcd.ArgumentsDelta)) state.Args.Append(tcd.ArgumentsDelta);
                        break;

                    case TokenUsage usage:
                        foreach (var flushed in FlushAccumulated(typeof(TokenUsage))) yield return flushed;
                        yield return usage;
                        break;

                    case FinishReason reason:
                        foreach (var flushed in FlushAccumulated(typeof(FinishReason))) yield return flushed;
                        yield return reason;
                        break;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        foreach (var flushed in FlushAccumulated(typeof(object))) yield return flushed;

        edi?.Throw();
    }
}
