using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Schema;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace AgentCore.Tests;

public class MockLLMProvider : ILLMProvider
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<IContentDelta>>> _responses = new();
    
    public int? ContextWindow { get; set; } = 2000;
    
    public List<IReadOnlyList<Message>> CapturedMessages { get; } = new();
    public List<IReadOnlyList<Tool>?> CapturedTools { get; } = new();
    public List<JsonSchema?> CapturedResponseSchemas { get; } = new();
    
    public int CallCount => CapturedMessages.Count;

    public void Enqueue(Func<CancellationToken, IAsyncEnumerable<IContentDelta>> generator)
    {
        _responses.Enqueue(generator);
    }

    public void Enqueue(params IContentDelta[] deltas)
    {
        _responses.Enqueue(ct => ToAsyncEnumerable(deltas, ct));
    }

    public void Enqueue(IEnumerable<IContentDelta> deltas)
    {
        _responses.Enqueue(ct => ToAsyncEnumerable(deltas, ct));
    }

    public void EnqueueException(Exception ex)
    {
        _responses.Enqueue(ct => ThrowException(ex));
    }

    private static async IAsyncEnumerable<IContentDelta> ToAsyncEnumerable(
        IEnumerable<IContentDelta> deltas, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var delta in deltas)
        {
            ct.ThrowIfCancellationRequested();
            yield return delta;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IContentDelta> ThrowException(Exception ex)
    {
        if (false) yield break;
        throw ex;
    }

    public IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default)
    {
        CapturedMessages.Add(messages.ToList());
        CapturedTools.Add(tools?.ToList());
        CapturedResponseSchemas.Add(options?.ResponseSchema);

        if (_responses.Count == 0)
        {
            return ToAsyncEnumerable(Enumerable.Empty<IContentDelta>(), ct);
        }

        var generator = _responses.Dequeue();
        return generator(ct);
    }
}

public class MockMemory : IMemoryService
{
    public List<Message> History { get; } = new();

    public Task<IReadOnlyList<Message>> RecallAsync(Message currentInput, int? maxTokens, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<Message>>(History.ToList());
    }

    public Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        History.AddRange(completedTurn);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        History.Clear();
        return Task.CompletedTask;
    }
}

public class MockTooling : IToolService
{
    public Func<IEnumerable<ToolCall>, CancellationToken, Task<IReadOnlyList<Message>>> Handler { get; set; } =
        (calls, ct) => Task.FromResult<IReadOnlyList<Message>>(
            calls.Select(c => new Message(Role.Tool, new ToolResult(c.Id, new Text("Success")))).ToList()
        );

    public Task<IReadOnlyList<Message>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
    {
        return Handler(calls, ct);
    }
}
