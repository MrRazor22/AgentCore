using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.Layers.Context;
using AgentCore.Layers.Context.Store;
using AgentCore.Layers.LLM;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using AgentCore.Tool.Tools;
using Microsoft.Data.Sqlite;

namespace EvolutionAgentCore;

public sealed class MockEvolutionLLM : ILLM
{
    private readonly Func<IReadOnlyList<Message>, IReadOnlyList<ToolDefinition>?, int, IAsyncEnumerable<IMessageEvent>> _handler;
    private int _callCount = 0;
    public int CallCount => _callCount;

    public MockEvolutionLLM(Func<IReadOnlyList<Message>, IReadOnlyList<ToolDefinition>?, int, IAsyncEnumerable<IMessageEvent>> handler)
    {
        _handler = handler;
    }

    public async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _callCount++;
        await foreach (var evt in _handler(messages, tools, _callCount).WithCancellation(ct))
        {
            yield return evt;
        }
    }
}

public sealed class WeatherTool : ITool
{
    public ToolDefinition Definition => new(
        "get_weather",
        "Get current weather for a city",
        new JsonSchema(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["city"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "city" }
        })
    );

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var city = arguments.TryGetPropertyValue("city", out var c) ? c?.ToString() : "unknown";
        yield return new Text($"Weather in {city}: 72F and Sunny");
    }
}

public sealed class SensitiveExecuteTool : ITool
{
    public ToolDefinition Definition => new(
        "execute_command",
        "Executes sensitive command",
        new JsonSchema(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["command"] = new JsonObject { ["type"] = "string" }
            }
        })
    );

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var cmd = arguments.TryGetPropertyValue("command", out var c) ? c?.ToString() : "";
        yield return new Text($"Executed: {cmd} with status 0");
    }
}

public sealed class AgentTool : ITool
{
    private readonly IAgent _child;
    public ToolDefinition Definition { get; }

    public AgentTool(string name, string description, IAgent child)
    {
        _child = child;
        Definition = new ToolDefinition(
            name,
            description,
            new JsonSchema(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray { "query" }
            })
        );
    }

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = arguments.TryGetPropertyValue("query", out var q) ? q?.ToString() ?? "" : "";
        var chunks = new List<string>();
        await foreach (var evt in _child.InvokeStreamingAsync([new Text(query)], ct).ConfigureAwait(false))
        {
            if (evt is Text t) chunks.Add(t.Value);
        }
        yield return new Text(string.Join("", chunks));
    }
}

public sealed class SqliteChatStore : IChatStore, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteChatStore(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS messages (id INTEGER PRIMARY KEY AUTOINCREMENT, role TEXT NOT NULL, content TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    public Task<IReadOnlyList<Message>?> LoadAsync(CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT role, content FROM messages ORDER BY id ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<Message>();
        while (reader.Read())
        {
            var roleStr = reader.GetString(0);
            var contentStr = reader.GetString(1);
            var role = roleStr == "user" ? Role.User : (roleStr == "assistant" ? Role.Assistant : Role.System);
            list.Add(new Message(role, [new Text(contentStr)]));
        }
        return Task.FromResult<IReadOnlyList<Message>?>(list);
    }

    public Task AppendAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return Task.CompletedTask;
        using var transaction = _connection.BeginTransaction();
        foreach (var msg in messages)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO messages (role, content) VALUES (@r, @c);";
            cmd.Parameters.AddWithValue("@r", msg.Role.ToString().ToLowerInvariant());
            var text = string.Join("", msg.Contents.OfType<Text>().Select(t => t.Value));
            cmd.Parameters.AddWithValue("@c", text);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    public void Dispose() => _connection.Dispose();
}

public sealed class CompactingContextLayer(int threshold, IContext? inner = null) : ContextLayer(inner)
{
    public override async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        var messages = await base.ReadAsync(ct).ConfigureAwait(false);
        if (messages.Count > threshold)
        {
            var systemMsg = messages.FirstOrDefault(m => m.Role == Role.System);
            var recentMsg = messages.Last();
            var compacted = new List<Message>();
            if (systemMsg != null) compacted.Add(systemMsg);
            compacted.Add(new Message(Role.System, [new Text($"[Summary of {messages.Count - 2} earlier conversation turns]")]));
            compacted.Add(recentMsg);
            return compacted;
        }
        return messages;
    }
}

public sealed class InMemoryChatStore : IChatStore
{
    private readonly List<Message> _messages = [];
    public Task<IReadOnlyList<Message>?> LoadAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Message>?>(_messages.ToList());
    public Task AppendAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        _messages.AddRange(messages);
        return Task.CompletedTask;
    }
}

public sealed class EvolutionApprovalLayer : ToolboxLayer
{
    private readonly Func<ToolCall, bool> _approver;
    public EvolutionApprovalLayer(Func<ToolCall, bool> approver, IToolbox? inner = null) : base(inner)
    {
        _approver = approver;
    }

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var allowed = new List<ToolCall>();
        foreach (var call in calls)
        {
            if (!_approver(call))
            {
                yield return new MessageStart(Role.Tool, Id: call.Id);
                yield return new MessageDelta(call.Id, Content: new ToolResult(call.Id, [new Text("Tool execution was rejected by user.")], isError: true));
                yield return new MessageEnd(Id: call.Id);
            }
            else
            {
                allowed.Add(call);
            }
        }
        if (allowed.Count > 0 && Inner != null)
        {
            await foreach (var evt in Inner.ExecuteAsync(allowed, ct))
            {
                yield return evt;
            }
        }
    }
}

public sealed class EvolutionGuardrailLayer : LLMLayer
{
    private readonly Func<string, (bool IsValid, string Reason)> _validator;
    public EvolutionGuardrailLayer(Func<string, (bool IsValid, string Reason)> validator, ILLM? inner = null) : base(inner)
    {
        _validator = validator;
    }

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                var text = content is Text t ? t.Value : content.ToString() ?? string.Empty;
                var (isValid, reason) = _validator(text);
                if (!isValid)
                {
                    var id = Guid.NewGuid().ToString("N");
                    yield return new MessageStart(Role.Assistant, Id: id);
                    yield return new MessageDelta(id, Content: new Text(reason));
                    yield return new MessageEnd(Id: id);
                    yield break;
                }
            }
        }
        if (Inner != null)
        {
            await foreach (var evt in Inner.GenerateAsync(messages, tools, responseSchema, ct))
            {
                yield return evt;
            }
        }
    }
}
