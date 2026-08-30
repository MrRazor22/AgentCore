using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
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
                yield return new TextContentStart(blockIndex);
                yield return new TextContentDelta(blockIndex, t.Value);
                yield return new TextContentEnd(blockIndex);
                break;
            }
            case Reasoning r:
            {
                yield return new ReasoningContentStart(blockIndex);
                yield return new ReasoningContentDelta(blockIndex, r.Thought);
                yield return new ReasoningContentEnd(blockIndex);
                break;
            }
            case ToolCall tc:
            {
                int idx = blockIndex;
                var id = !string.IsNullOrEmpty(tc.Id) ? tc.Id : Guid.NewGuid().ToString("N");
                yield return new ToolCallContentStart(idx, id, tc.Name);
                var args = tc.Arguments?.ToJsonString() ?? "{}";
                if (!string.IsNullOrEmpty(args))
                {
                    yield return new ToolCallContentDelta(idx, args);
                }
                yield return new ToolCallContentEnd(idx);
                break;
            }
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




    public IAsyncEnumerable<IMessageEvent> StreamAsync(
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
    private readonly List<Message> _internalMessages = new();
    private readonly List<Message> _staged = new();

    public string RecallResult { get; set; } = "";

    public IReadOnlyList<Message> Messages
    {
        get
        {
            var list = new List<Message>();
            if (!string.IsNullOrEmpty(RecallResult))
            {
                list.Add(new Message(Role.System, [new Text(RecallResult)]));
            }
            list.AddRange(_internalMessages);
            return list;
        }
    }

    public Task StageAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct = default)
    {
        _staged.AddRange(messages);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Message>> PreparePromptAsync(
        CancellationToken ct = default)
    {
        var list = new List<Message>(Messages);
        list.AddRange(_staged);
        return Task.FromResult<IReadOnlyList<Message>>(list);
    }

    public Task CommitAsync(
        Message response,
        CancellationToken ct = default)
        => CommitAsync([response], ct);

    public Task CommitAsync(
        IReadOnlyList<Message> response,
        CancellationToken ct = default)
    {
        _internalMessages.Clear();
        var prompt = new List<Message>(Messages);
        prompt.AddRange(_staged);
        var messagesToStore = new List<Message>(prompt);
        if (!string.IsNullOrEmpty(RecallResult) && messagesToStore.Count > 0 && messagesToStore[0].Role == Role.System)
        {
            messagesToStore.RemoveAt(0);
        }
        _internalMessages.AddRange(messagesToStore);
        _internalMessages.AddRange(response);
        _staged.Clear();
        return Task.CompletedTask;
    }
}

public class MockTooling : ITooling
{
    private readonly List<Task<ToolResult>> _tasks = new();
    private readonly object _lock = new();

    public IReadOnlyList<ToolDefinition> Definitions { get; set; } = Array.Empty<ToolDefinition>();

    public IReadOnlyList<ToolDefinition> GetDefinitions() => Definitions;

    public Func<IEnumerable<ToolCall>, CancellationToken, Task<IReadOnlyList<ToolResult>>> Handler { get; set; } =
        (calls, ct) => Task.FromResult<IReadOnlyList<ToolResult>>(
            calls.Select(c => new ToolResult(c.Id, new Text("Success"))).ToList()
        );

    public Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        Task<ToolResult> task;
        try
        {
            task = Handler(new[] { call }, ct).ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception!.InnerException ?? t.Exception!;
                return t.Result[0];
            }, ct, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            task = Task.FromException<ToolResult>(ex);
        }

        lock (_lock)
        {
            _tasks.Add(task);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ToolResult> StreamResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<Task<ToolResult>> tasks;
        lock (_lock)
        {
            tasks = new List<Task<ToolResult>>(_tasks);
            _tasks.Clear();
        }

        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(completed);
            yield return await completed.ConfigureAwait(false);
        }
    }
}
