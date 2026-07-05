using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using System.Text.Json.Nodes;

namespace AgentCore.Tests;

public class MockLLMProvider : ILLMProvider
{
    private readonly Queue<Func<IEnumerable<IContentDelta>>> _actions = new();
    public int CallCount { get; private set; }

    public void EnqueueAction(Func<IEnumerable<IContentDelta>> action) => _actions.Enqueue(action);

    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        CallCount++;
        var action = _actions.Count > 0 ? _actions.Dequeue() : () => Enumerable.Empty<IContentDelta>();
        
        var result = action();
        foreach (var item in result)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            yield return item;
        }
        await Task.CompletedTask;
    }
}

public class MockToolRegistry : IToolRegistry
{
    public List<Tool> RegisteredTools { get; } = new();
    public IReadOnlyList<Tool> Tools => RegisteredTools;

    public void Register(Delegate del, string? name = null, string? description = null)
    {
        // Simple registration mock
        RegisteredTools.Add(new Tool
        {
            Name = name ?? del.Method.Name,
            Description = description ?? "",
            ParametersSchema = new JsonObject(),
            Invoker = args =>
            {
                var result = del.DynamicInvoke(args);
                return Task.FromResult<object?>(result?.ToString() ?? "");
            }
        });
    }

    public void Register(Tool tool) => RegisteredTools.Add(tool);
    
    public bool Unregister(string toolName)
    {
        var tool = RegisteredTools.FirstOrDefault(t => t.Name == toolName);
        if (tool != null)
        {
            RegisteredTools.Remove(tool);
            return true;
        }
        return false;
    }

    public Tool? TryGet(string toolName) => RegisteredTools.FirstOrDefault(t => t.Name == toolName);
}

public class MockTokenManager : ITokenManager
{
    public List<TokenUsage> LoggedUsage { get; } = new();
    public void Record(TokenUsage usage) => LoggedUsage.Add(usage);
    public TokenUsage GetTotals()
    {
        int input = LoggedUsage.Sum(u => u.InputTokens);
        int output = LoggedUsage.Sum(u => u.OutputTokens);
        int reasoning = LoggedUsage.Sum(u => u.ReasoningTokens);
        return new TokenUsage(input, output, reasoning);
    }
}

public class MockToolExecutor : IToolExecutor
{
    public Func<ToolCall, CancellationToken, Task<ToolResult>> Handler { get; set; } = 
        (call, ct) => Task.FromResult(new ToolResult(call.Id, new Text("Success")));

    public Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
    {
        return Handler(call, ct);
    }
}

public class MockMemory : IMemory
{
    public List<Message> History { get; } = new();
    public int RecallCount { get; private set; }
    public int RememberCount { get; private set; }

    public Task<IReadOnlyList<Message>> RecallAsync(Message currentInput, TokenBudget budget, CancellationToken ct = default)
    {
        RecallCount++;
        return Task.FromResult<IReadOnlyList<Message>>(History);
    }

    public Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        RememberCount++;
        History.AddRange(completedTurn);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        History.Clear();
        return Task.CompletedTask;
    }
}
