using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System.Runtime.CompilerServices;

namespace AgentCore.Tests;

public class MockLLMProvider : ILLM
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ILLMOutput>>> _responses = new();

    public int ContextWindow { get; set; } = 4096;
    public int ReservedTokens { get; set; } = 512;

    public List<IReadOnlyList<Message>> CapturedMessages { get; } = new();
    public List<IReadOnlyList<ToolDefinition>?> CapturedTools { get; } = new();
    public List<JsonSchema?> CapturedResponseSchemas { get; } = new();

    public int CallCount => CapturedMessages.Count;

    public void Enqueue(Func<CancellationToken, IAsyncEnumerable<ILLMOutput>> generator)
    {
        _responses.Enqueue(generator);
    }

    private static IEnumerable<ILLMOutput> ConvertToEvents(object evt)
    {
        switch (evt)
        {
            case Text t:
                yield return new TextDelta(t.Value);
                yield return new TextEnd();
                break;
            case Reasoning r:
                yield return new ReasoningDelta(r.Thought);
                yield return new ReasoningEnd();
                break;
            case ToolCall tc:
            {
                var id = !string.IsNullOrEmpty(tc.Id) ? tc.Id : Guid.NewGuid().ToString("N");
                yield return new ToolCallStart(id, tc.Name, tc.Index);
                var args = tc.Arguments?.ToJsonString() ?? tc.RawArguments ?? "";
                if (!string.IsNullOrEmpty(args))
                {
                    yield return new ToolCallDelta(id, args, tc.Index);
                }
                yield return new ToolCallEnd(id, tc.Index);
                break;
            }
            case ILLMOutput output:
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

    private static async IAsyncEnumerable<ILLMOutput> ThrowException(Exception ex, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        throw ex;
        yield break;
    }

    private static async IAsyncEnumerable<ILLMOutput> ToAsyncEnumerable(IEnumerable<object> items, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            foreach (var evt in ConvertToEvents(item))
            {
                yield return evt;
            }
        }
    }




    public async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        CapturedMessages.Add(messages.ToList());
        CapturedTools.Add(tools);
        CapturedResponseSchemas.Add(responseSchema);

        var generator = _responses.Count > 0 ? _responses.Dequeue() : (ct => ToAsyncEnumerable(Enumerable.Empty<ILLMOutput>(), ct));
        await foreach (var item in generator(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            yield return item;
        }
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
        IReadOnlyList<Message> response,
        TokenUsage? usage = null,
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
