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

    private static ILLMOutput ConvertToDelta(object evt)
    {
        return evt switch
        {
            Text t => new TextDelta(t.Value, IsFinal: true),
            Reasoning r => new ReasoningDelta(r.Thought, IsFinal: true),
            ToolCall tc => new ToolCallDelta(tc.Id, tc.Name, tc.Arguments?.ToJsonString(), IsFinal: true),
            ILLMOutput output => output,
            _ => throw new NotSupportedException()
        };
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
        var list = items.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            await Task.Yield();
            var item = list[i];
            var converted = ConvertToDelta(item);

            if (converted is IContentDelta cd)
            {
                bool hasSubsequentSameStream = list.Skip(i + 1).Any(next =>
                {
                    var nextConverted = ConvertToDelta(next);
                    if (nextConverted is not IContentDelta nextCd) return false;
                    if (!string.IsNullOrEmpty(cd.Id) && !string.IsNullOrEmpty(nextCd.Id))
                        return string.Equals(cd.Id, nextCd.Id, StringComparison.Ordinal);
                    if (cd.Index.HasValue && nextCd.Index.HasValue)
                        return cd.Index.Value == nextCd.Index.Value;
                    return cd.GetType() == nextCd.GetType();
                });

                if (!hasSubsequentSameStream)
                {
                    converted = cd switch
                    {
                        TextDelta td => td with { IsFinal = true },
                        ReasoningDelta rd => rd with { IsFinal = true },
                        ToolCallDelta tcd => tcd with { IsFinal = true },
                        _ => converted
                    };
                }
            }

            yield return converted;
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
