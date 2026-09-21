using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCoreT12;

public sealed class DynamicToolboxLayer : ToolboxLayer
{
    private readonly Dictionary<string, (ITool Tool, HashSet<string>? AllowedRoles, bool IsActive)> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public DynamicToolboxLayer(IToolbox? inner = null) : base(inner)
    {
    }

    public void RegisterTool(ITool tool, IEnumerable<string>? allowedRoles = null, bool isActive = true)
    {
        lock (_lock)
        {
            var roles = allowedRoles != null ? new HashSet<string>(allowedRoles, StringComparer.OrdinalIgnoreCase) : null;
            _tools[tool.Definition.Name] = (tool, roles, isActive);
        }
    }

    public void SetToolActive(string toolName, bool isActive)
    {
        lock (_lock)
        {
            if (_tools.TryGetValue(toolName, out var entry))
            {
                _tools[toolName] = (entry.Tool, entry.AllowedRoles, isActive);
            }
        }
    }

    public override ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
        => GetToolsAsync(role: null, ct);

    public ValueTask<IReadOnlyList<ITool>> GetToolsAsync(string? role, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var list = new List<ITool>();
            foreach (var (_, (tool, roles, isActive)) in _tools)
            {
                if (!isActive) continue;
                if (role != null && roles != null && !roles.Contains(role)) continue;
                list.Add(tool);
            }
            return ValueTask.FromResult<IReadOnlyList<ITool>>(list);
        }
    }

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var delegatedCalls = new List<ToolCall>();

        foreach (var call in calls)
        {
            ITool? targetTool = null;
            bool isDeactivated = false;

            lock (_lock)
            {
                if (_tools.TryGetValue(call.Name, out var entry))
                {
                    if (entry.IsActive)
                    {
                        targetTool = entry.Tool;
                    }
                    else
                    {
                        isDeactivated = true;
                    }
                }
            }

            if (isDeactivated)
            {
                yield return new MessageStart(Role.Tool, Id: call.Id);
                yield return new MessageDelta(call.Id, Content: new ToolResult(
                    call.Id,
                    [new Text($"Error: Tool '{call.Name}' is currently deactivated.")],
                    isError: true));
                yield return new MessageEnd(Id: call.Id);
                continue;
            }

            if (targetTool != null)
            {
                var args = !string.IsNullOrWhiteSpace(call.Arguments)
                    ? JsonNode.Parse(call.Arguments)?.AsObject() ?? new JsonObject()
                    : new JsonObject();

                yield return new MessageStart(Role.Tool, Id: call.Id);
                var results = new List<IToolResultContent>();
                await foreach (var evt in targetTool.InvokeStreamingAsync(args, ct))
                {
                    if (evt is IToolResultContent trc)
                    {
                        results.Add(trc);
                    }
                    else if (evt is Text t)
                    {
                        results.Add(t);
                    }
                }
                yield return new MessageDelta(call.Id, Content: new ToolResult(call.Id, results));
                yield return new MessageEnd(Id: call.Id);
            }
            else
            {
                delegatedCalls.Add(call);
            }
        }

        if (delegatedCalls.Count > 0 && Inner != null)
        {
            await foreach (var evt in Inner.ExecuteAsync(delegatedCalls, ct))
            {
                yield return evt;
            }
        }
    }
}
