using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tools;
using AgentCore.LLM.Schema;
using AgentCore.LLM.Chat;

namespace AgentCore.Tests;

public class MockLLMProvider : ILLM
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<IContentDelta>>> _responses = new();
    
    public int ContextWindow { get; set; } = 2000;
    
    public LLMCapabilities GetCapabilities()
    {
        return new LLMCapabilities { ContextWindow = ContextWindow, ReservedTokens = 2048 };
    }
    
    public List<IReadOnlyList<Message>> CapturedMessages { get; } = new();
    public List<IReadOnlyList<Tool>?> CapturedTools { get; } = new();
    public List<JsonSchema?> CapturedResponseSchemas { get; } = new();
    
    public int CallCount => CapturedMessages.Count;

    public void Enqueue(Func<CancellationToken, IAsyncEnumerable<IContentDelta>> generator)
    {
        _responses.Enqueue(generator);
    }

    private static IContentDelta ConvertToDelta(object evt)
    {
        return evt switch
        {
            IContentDelta delta => delta,
            Text text => new TextDelta(text.Value),
            Reasoning reasoning => new ReasoningDelta(reasoning.Thought),
            ToolCall toolCall => new ToolCallDelta(0, toolCall.Id, toolCall.Name, toolCall.Arguments.ToString()),
            MetaDataEvent meta => new MetaDelta(meta.FinishReason),
            TokenUsage usage => new MetaDelta(null, usage.InputTokens, usage.OutputTokens, usage.ReasoningTokens),
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

public class MockMemoryProvider : IMemory
{
    public List<string> Saved { get; } = new();
    public string RecallResult { get; set; } = "";

    public Task RememberAsync(string content, CancellationToken ct = default)
    {
        Saved.Add(content);
        return Task.CompletedTask;
    }

    public Task<string> RecallAsync(string query, CancellationToken ct = default)
    {
        return Task.FromResult(RecallResult);
    }
}

public class MockContextService : IContextService
{
    public List<Message> History { get; } = new();

    public Task<List<Message>> PrepareConversationAsync(
        IContent? instructions,
        Message userInput,
        IReadOnlyList<Tool> tools,
        CancellationToken ct = default)
    {
        var list = new List<Message>();
        if (instructions != null) list.Add(new Message(Role.System, instructions));
        list.AddRange(History);
        list.Add(userInput);
        return Task.FromResult(list);
    }

    public Task UpdateHistoryAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        History.AddRange(completedTurn);
        return Task.CompletedTask;
    }
}

public class MockTooling : IToolService
{
    public IReadOnlyList<Tool> ToolList { get; set; } = Array.Empty<Tool>();
    public IReadOnlyList<Tool> GetTools() => ToolList;

    public Func<IEnumerable<ToolCall>, CancellationToken, Task<IReadOnlyList<Message>>> Handler { get; set; } =
        (calls, ct) => Task.FromResult<IReadOnlyList<Message>>(
            calls.Select(c => new Message(Role.Tool, new ToolResult(c.Id, new Text("Success")))).ToList()
        );

    public Task<IReadOnlyList<Message>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
    {
        return Handler(calls, ct);
    }
}
