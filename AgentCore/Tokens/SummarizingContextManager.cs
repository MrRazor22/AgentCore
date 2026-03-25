using AgentCore.Conversation;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Tokens;

public sealed class SummarizingContextManager(
    ITokenCounter _counter,
    ILogger<SummarizingContextManager> _logger,
    ILLMProvider? _provider = null,
    string summaryPrompt = "Extract and summarize the core persistent facts, database credentials, specific user preferences, and prior tool results from this history. Create a concise scratchpad."
) : IContextManager
{
    private Message? _summaryMessage;
    private readonly string _summaryPrompt = summaryPrompt;

    public async Task<Chat> ReduceAsync(Chat chat, int totalTokens, LLMOptions options, CancellationToken ct = default)
    {
        int ctxLen = options.ContextLength ?? throw new InvalidOperationException("ContextLength is required.");

        int currentTokens = await _counter.CountAsync(
            ToMessageList(chat), ct).ConfigureAwait(false);

        double usage = (double)currentTokens / ctxLen;
        if (usage < 0.30) return chat;

        var (compressible, tail) = await SplitTail(chat.Turns, (int)(ctxLen * 0.35), ct);

        var staged = compressible.ToList();
        var applied = new List<string>();

        if (usage >= 0.30)
        {
            staged = staged.Select(TruncateOutputs).ToList();
            applied.Add("truncate");
        }

        if (usage >= 0.50)
        {
            staged = staged.Select(DropToolSteps).ToList();
            applied.Add("drop-steps");
        }

        if (usage >= 0.70)
        {
            staged = staged.Select(t => new Turn(t.User, [], null)).ToList();
            applied.Add("skeleton");
        }

        if (usage >= 0.85 && _provider != null)
        {
            await Stage4_Summarize(staged, options, ct);
            applied.Add("summarize");
            staged = [];
        }

        var result = new Chat();
        foreach (var ex in staged.Concat(tail))
            if (ex.User.Content is not Text t || t.Value.Length > 0)
                result.Add(ex);

        int after = await _counter.CountAsync(ToMessageList(result), ct).ConfigureAwait(false);
        _logger.LogDebug("Compacted [{Stages}]: {Before}→{After} ({Saved:P0})",
            string.Join(",", applied), currentTokens, after, 1.0 - (double)after / currentTokens);

        return result;
    }

    private static Turn TruncateOutputs(Turn t, int maxChars) =>
        new(t.User, t.ToolSteps.Select(s => TruncateResult(s, maxChars)).ToList(), t.AssistantReply);

    private static Turn DropToolSteps(Turn t) => new(t.User, [], t.AssistantReply);

    private static (Message Call, Message Result) TruncateResult((Message Call, Message Result) step, int maxChars)
    {
        if (step.Result.Content is ToolResult tr)
        {
            var text = tr.Result?.ForLlm() ?? "";
            if (text.Length > maxChars)
                return (step.Call, new Message(step.Result.Role,
                    new ToolResult(tr.CallId, new Text(text[..maxChars] + "...[truncated]"))));
        }
        return step;
    }

    private static IEnumerable<Message> ToMessageList(Chat chat)
    {
        var result = new List<Message>();
        foreach (var turn in chat.Turns)
        {
            result.Add(turn.User);
            foreach (var (call, resultMsg) in turn.ToolSteps) { result.Add(call); result.Add(resultMsg); }
            if (turn.AssistantReply != null) result.Add(turn.AssistantReply);
        }
        return result;
    }

    private async Task<(List<Turn> compressible, List<Turn> tail)> SplitTail(IReadOnlyList<Turn> turns, int targetTokens, CancellationToken ct)
    {
        int tokenCount = 0;
        int boundaryIndex = 0;

        for (int i = turns.Count - 1; i >= 0; i--)
        {
            var turn = turns[i];
            var messages = TurnToMessageList(turn);
            foreach (var msg in messages)
            {
                tokenCount += await _counter.CountAsync([msg], ct).ConfigureAwait(false);
            }
            if (tokenCount >= targetTokens)
            {
                boundaryIndex = i + 1;
                break;
            }
        }

        return (turns.Take(boundaryIndex).ToList(), turns.Skip(boundaryIndex).ToList());
    }

    private static IEnumerable<Message> TurnToMessageList(Turn turn)
    {
        yield return turn.User;
        foreach (var (call, result) in turn.ToolSteps) { yield return call; yield return result; }
        if (turn.AssistantReply != null) yield return turn.AssistantReply;
    }

    private async Task Stage4_Summarize(List<Turn> turns, LLMOptions options, CancellationToken ct)
    {
        var content = turns.SelectMany(TurnToMessageList)
                           .Select(m => $"[{m.Role}]: {m.Content?.ForLlm() ?? ""}");

        var input = new List<Message> { new(Role.System, new Text(_summaryPrompt)) };
        if (_summaryMessage != null) input.Add(_summaryMessage);
        input.Add(new(Role.User, new Text(string.Join("\n\n", content))));

        var sb = new StringBuilder();
        await foreach (var evt in _provider!.StreamAsync(input,
            new LLMOptions { Model = options.Model, MaxOutputTokens = 1024, ContextLength = options.ContextLength },
            null, ct))
            if (evt is TextDelta td) sb.Append(td.Value);

        _summaryMessage = new Message(Role.Assistant, new Summary(sb.ToString().Trim()));
    }
}
