using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.Context;
using AgentCore.Tools;
using AgentCore.LLM.Schema;
using AgentCore.LLM.Chat;

namespace AgentCore.Tests;

public class MockLLMProvider : ILLM
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ILLMOutput>>> _responses = new();
    
    public int ContextWindow { get; set; } = 4096;
    public int ReservedTokens { get; set; } = 512;
    
    public LLMCapabilities GetCapabilities()
    {
        return new LLMCapabilities { ContextWindow = ContextWindow, ReservedTokens = ReservedTokens };
    }
    
    public List<IReadOnlyList<Message>> CapturedMessages { get; } = new();
    public List<IReadOnlyList<Tool>?> CapturedTools { get; } = new();
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
            ILLMOutput output => output,
            Text t => new TextDelta(t.Value),
            Reasoning r => new ReasoningDelta(r.Thought),
            ToolCall tc => new ToolCallDelta(tc.Id, tc.Name, tc.Arguments?.ToJsonString()),
            string str => new TextDelta(str),
            _ => throw new ArgumentException($"Unknown event type {evt.GetType()}")
        };
    }

    public void Enqueue(params object[] events)
    {
        var deltas = events.Select(ConvertToDelta).ToList();
        _responses.Enqueue(ct => ToAsyncEnumerable(deltas, ct));
    }

    public void Enqueue(IEnumerable<object> events)
    {
        var deltas = events.Select(ConvertToDelta).ToList();
        _responses.Enqueue(ct => ToAsyncEnumerable(deltas, ct));
    }

    public void EnqueueException(Exception ex)
    {
        _responses.Enqueue(ct => ThrowException(ex));
    }

    private static async IAsyncEnumerable<ILLMOutput> ToAsyncEnumerable(
        IEnumerable<ILLMOutput> deltas, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var delta in deltas)
        {
            ct.ThrowIfCancellationRequested();
            yield return delta;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ILLMOutput> ThrowException(Exception ex)
    {
        if (false) yield break;
        throw ex;
    }

    public IAsyncEnumerable<ILLMOutput> StreamAsync(
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
            return ToAsyncEnumerable(Enumerable.Empty<ILLMOutput>(), ct);
        }

        var generator = _responses.Dequeue();
        return generator(ct);
    }
}

public class MockMemoryProvider : IContext
{
    private readonly List<Message> _internalMessages = new();

    public string RecallResult { get; set; } = "";

    public IReadOnlyList<Message> Messages
    {
        get
        {
            var list = new List<Message>();
            if (!string.IsNullOrEmpty(RecallResult))
            {
                list.Add(new Message(Role.System, new Text(RecallResult)));
            }
            list.AddRange(_internalMessages);
            return list;
        }
    }

    public Task AddAsync(Message message, CancellationToken ct = default)
    {
        _internalMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _internalMessages.Clear();
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        if (messages != null)
        {
            _internalMessages.AddRange(messages);
        }
        return Task.CompletedTask;
    }
}

public class MockTooling : ITooling
{
    public IReadOnlyList<Tool> Tools { get; set; } = Array.Empty<Tool>();

    public Func<IEnumerable<ToolCall>, CancellationToken, Task<IReadOnlyList<Message>>> Handler { get; set; } =
        (calls, ct) => Task.FromResult<IReadOnlyList<Message>>(
            calls.Select(c => new Message(Role.Tool, new ToolResult(c.Id, new Text("Success")))).ToList()
        );

    public Task<IReadOnlyList<Message>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
    {
        return Handler(calls, ct);
    }
}
