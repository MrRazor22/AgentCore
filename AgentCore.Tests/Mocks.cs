using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;
using System.Runtime.CompilerServices;

namespace AgentCore.Tests;

public class MockLLMProvider : ILLM
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<IMessageEvent>>> _responses = new();

    public int ContextWindow { get; set; } = 4096;
    public int ReservedTokens { get; set; } = 512;

    public List<IReadOnlyList<Message>> CapturedMessages { get; } = new();
    public List<IReadOnlyList<ToolDefinition>?> CapturedTools { get; } = new();
    public List<JsonSchema?> CapturedResponseSchemas { get; } = new();

    public int CallCount => CapturedMessages.Count;

    public void Enqueue(Func<CancellationToken, IAsyncEnumerable<IMessageEvent>> generator)
    {
        _responses.Enqueue(generator);
    }

    private static IEnumerable<IMessageEvent> ConvertToEvents(object evt, int blockIndex)
    {
        switch (evt)
        {
            case Text t:
            {
                yield return new MessageDelta(Content: new TextStart(blockIndex));
                yield return new MessageDelta(Content: new TextDelta(blockIndex, t.Value));
                yield return new MessageDelta(Content: new TextEnd(blockIndex));
                break;
            }
            case Reasoning r:
            {
                yield return new MessageDelta(Content: new ReasoningStart(blockIndex));
                yield return new MessageDelta(Content: new ReasoningDelta(blockIndex, r.Thought));
                yield return new MessageDelta(Content: new ReasoningEnd(blockIndex));
                break;
            }
            case ToolCall tc:
            {
                int idx = blockIndex;
                var id = !string.IsNullOrEmpty(tc.Id) ? tc.Id : Guid.NewGuid().ToString("N");
                yield return new MessageDelta(Content: new ToolCallStart(idx, id, tc.Name));
                var args = tc.Arguments?.ToJsonString() ?? "{}";
                if (!string.IsNullOrEmpty(args))
                {
                    yield return new MessageDelta(Content: new ToolCallDelta(idx, args));
                }
                yield return new MessageDelta(Content: new ToolCallEnd(idx));
                break;
            }
            case IContentEvent ce:
                yield return new MessageDelta(Content: ce);
                break;
            case IMessageEvent output:
                yield return output;
                break;
            default:
                throw new NotSupportedException($"Unsupported mock item type {evt?.GetType().FullName}");
        }
    }

    public void Enqueue(params object[] items)
    {
        Enqueue(ct => ToAsyncEnumerable(items, ct));
    }

    public void EnqueueSimpleText(string text)
    {
        Enqueue(new Text(text));
    }

    public void EnqueueException(Exception ex)
    {
        Enqueue(ct => ThrowException(ex, ct));
    }

    private static async IAsyncEnumerable<IMessageEvent> ThrowException(Exception ex, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<IMessageEvent> ToAsyncEnumerable(IEnumerable<object> items, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new MessageStart(Role.Assistant);
        int blockIndex = 0;
        bool hasEmittedEnd = false;
        foreach (var item in items)
        {
            await Task.Yield();
            foreach (var evt in ConvertToEvents(item, blockIndex))
            {
                if (evt is MessageEnd) hasEmittedEnd = true;
                yield return evt;
            }
            if (item is Text or Reasoning or ToolCall)
            {
                blockIndex++;
            }
        }
        if (!hasEmittedEnd)
        {
            yield return new MessageEnd();
        }
    }




    public IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        CapturedMessages.Add(messages.ToList());
        CapturedTools.Add(tools);
        CapturedResponseSchemas.Add(responseSchema);

        var generator = _responses.Count > 0 ? _responses.Dequeue() : (ct => ToAsyncEnumerable(Enumerable.Empty<IMessageEvent>(), ct));
        return generator(ct);
    }
}

public class MockMemoryProvider : IContext
{
    private readonly ChatContext _inner = new();

    public string RecallResult { get; set; } = "";

    public IReadOnlyList<Message> Messages => PrepareAsync().GetAwaiter().GetResult();

    public async Task<IReadOnlyList<Message>> PrepareAsync(
        IEnumerable<Message>? messages = null,
        CancellationToken ct = default)
    {
        var list = new List<Message>();
        if (!string.IsNullOrEmpty(RecallResult))
        {
            list.Add(new Message(Role.System, [new Text(RecallResult)]));
        }
        list.AddRange(await _inner.PrepareAsync(messages, ct));
        return list;
    }

    public IAsyncEnumerable<IContentEvent> IngestAsync(
        IAsyncEnumerable<IMessageEvent> events,
        CancellationToken ct = default)
        => _inner.IngestAsync(events, ct);
}

public class MockTooling : IToolbox
{
    public IReadOnlyList<ToolDefinition> Definitions { get; set; } = Array.Empty<ToolDefinition>();

    public IReadOnlyList<ToolDefinition> GetDefinitions() => Definitions;

    public Func<IEnumerable<ToolCall>, CancellationToken, Task<IReadOnlyList<IContent>>> Handler { get; set; } =
        (calls, ct) => Task.FromResult<IReadOnlyList<IContent>>(
            calls.Select(_ => (IContent)new Text("Success")).ToList()
        );

    public async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var results = await Handler(calls, ct).ConfigureAwait(false);
        for (int i = 0; i < calls.Count && i < results.Count; i++)
        {
            var call = calls[i];
            yield return new MessageStart(Role.Tool, Id: call.Id);
            yield return new MessageDelta(call.Id, Content: results[i], Metadata: new ToolCallId(call.Id));
            yield return new MessageEnd(call.Id);
        }
    }
}
