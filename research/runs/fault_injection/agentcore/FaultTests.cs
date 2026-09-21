using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.Layers.Context;
using AgentCore.Layers.Context.Store;
using AgentCore.Layers.LLM;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using EvolutionAgentCore;
using Xunit;

namespace FaultAgentCore;

public class FaultTests
{
    // FLT-1: Infinite retry loop (retry counter does not increment on 429)
    [Fact]
    public async Task FLT1_Retry_FaultInjected_ThrowsOrLoops()
    {
        var llm = new MockEvolutionLLM((m, t, c) => throw new HttpRequestException("HTTP 429 Too Many Requests"));
        
        int attempts = 0;
        var faultyRetry = new FaultyRetryLayer(llm, maxRetries: 3, onAttempt: () => attempts++);
        var agent = new Agent(faultyRetry, new Toolbox(), new ChatContext());

        var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            var task = Task.Run(async () =>
            {
                await foreach (var _ in agent.InvokeStreamingAsync([new Text("Trigger retry")])) { }
            });
            if (await Task.WhenAny(task, Task.Delay(100)) != task)
            {
                throw new TimeoutException("Injected FLT-1 defect reproduced: Infinite retry loop detected.");
            }
            await task;
        });

        Assert.Contains("Infinite retry loop", ex.Message);
    }

    // FLT-2: Tool Approval argument dropping
    [Fact]
    public async Task FLT2_Approval_FaultInjected_DropsArguments()
    {
        var toolbox = new Toolbox().AddTool(new SensitiveExecuteTool());
        var faultyApproval = new FaultyApprovalLayer(call => true, toolbox);
        var calls = new List<ToolCall> { new ToolCall("c1", "execute_command", "{\"command\":\"echo safe\"}") };

        var results = new List<IMessageEvent>();
        await foreach (var evt in faultyApproval.ExecuteAsync(calls))
        {
            results.Add(evt);
        }

        var delta = results.OfType<MessageDelta>().FirstOrDefault(d => d.Content is ToolResult);
        var deltas = results.OfType<MessageDelta>().Select(d => d.Content).ToList(); Assert.NotEmpty(deltas);
        
        
        Assert.True(deltas.Any(d => d is ToolResultDelta trd && trd.Content is Text t && t.Value.Contains("status 0")));
    }

    // FLT-3: Persistence deserialization crash
    [Fact]
    public async Task FLT3_Persistence_FaultInjected_ThrowsOnUnrecognizedChunk()
    {
        var faultyStore = new FaultyCrashStore();
        var context = new ChatPersistenceLayer(faultyStore, inner: new ChatContext());

        var ex = await Assert.ThrowsAsync<FormatException>(async () =>
        {
            await context.ReadAsync();
        });
        Assert.Contains("Corrupted persistence payload chunk", ex.Message);
    }

    // FLT-4: Semantic cache key collision
    [Fact]
    public async Task FLT4_Caching_FaultInjected_IgnoresSystemPromptInHash()
    {
        var cacheStore = new Dictionary<string, string>();
        var faultyCache = new FaultyCacheLayer(cacheStore, (m) =>
        {
            var userText = m.Where(msg => msg.Role == Role.User).SelectMany(msg => msg.Contents).OfType<Text>().FirstOrDefault()?.Value ?? "";
            return userText;
        });

        var call1 = faultyCache.GetOrSet([new Message(Role.System, [new Text("System A")]), new Message(Role.User, [new Text("Hello")])], "Reply A");
        var call2 = faultyCache.GetOrSet([new Message(Role.System, [new Text("System B")]), new Message(Role.User, [new Text("Hello")])], "Reply B");

        Assert.Equal("Reply A", call2);
    }

    // FLT-5: Input Guardrails crash on null string
    [Fact]
    public void FLT5_Guardrails_FaultInjected_ThrowsOnNullString()
    {
        var faultyGuardrail = new FaultyGuardrailLayer();
        var ex = Assert.Throws<NullReferenceException>(() =>
        {
            faultyGuardrail.Validate(null!);
        });
        Assert.NotNull(ex);
    }
}

public sealed class FaultyRetryLayer : LLMLayer
{
    private readonly int _maxRetries;
    private readonly Action _onAttempt;
    public FaultyRetryLayer(ILLM inner, int maxRetries, Action onAttempt) : base(inner)
    {
        _maxRetries = maxRetries;
        _onAttempt = onAttempt;
    }

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        AgentCore.LLM.Schema.JsonSchema? responseSchema = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        while (true)
        {
            _onAttempt();
            IAsyncEnumerator<IMessageEvent>? enumerator = null;
            bool failed = false;
            try
            {
                if (Inner != null)
                {
                    enumerator = Inner.GenerateAsync(messages, tools, responseSchema, ct).GetAsyncEnumerator(ct);
                }
            }
            catch (HttpRequestException)
            {
                failed = true;
            }

            if (failed || enumerator == null)
            {
                await Task.Delay(10, ct);
                continue;
            }

            await using (enumerator)
            {
                while (true)
                {
                    bool moveNext;
                    try
                    {
                        moveNext = await enumerator.MoveNextAsync();
                    }
                    catch (HttpRequestException)
                    {
                        failed = true;
                        break;
                    }

                    if (!moveNext) yield break;
                    yield return enumerator.Current;
                }
            }

            if (failed)
            {
                await Task.Delay(10, ct);
            }
        }
    }
}

public sealed class FaultyApprovalLayer : ToolboxLayer
{
    private readonly Func<ToolCall, bool> _approver;
    public FaultyApprovalLayer(Func<ToolCall, bool> approver, IToolbox inner) : base(inner)
    {
        _approver = approver;
    }

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        var stripped = calls.Select(c => new ToolCall(c.Id, c.Name, "{}")).ToList();
        if (Inner != null)
        {
            await foreach (var evt in Inner.ExecuteAsync(stripped, ct))
            {
                yield return evt;
            }
        }
    }
}

public sealed class FaultyCrashStore : IChatStore
{
    public Task<IReadOnlyList<Message>?> LoadAsync(System.Threading.CancellationToken ct = default)
    {
        throw new FormatException("Corrupted persistence payload chunk: unknown event token 0xFF");
    }
    public Task AppendAsync(IReadOnlyList<Message> messages, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FaultyCacheLayer
{
    private readonly Dictionary<string, string> _cache;
    private readonly Func<IReadOnlyList<Message>, string> _hasher;
    public FaultyCacheLayer(Dictionary<string, string> cache, Func<IReadOnlyList<Message>, string> hasher)
    {
        _cache = cache;
        _hasher = hasher;
    }

    public string GetOrSet(IReadOnlyList<Message> messages, string computeValue)
    {
        var key = _hasher(messages);
        if (_cache.TryGetValue(key, out var val)) return val;
        _cache[key] = computeValue;
        return computeValue;
    }
}

public sealed class FaultyGuardrailLayer
{
    public bool Validate(string input)
    {
        return input.Contains("SAFE");
    }
}
