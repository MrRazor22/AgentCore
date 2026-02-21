using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.LLM.Handlers;

public sealed class ToolCallHandler(IToolCatalog _tools, IToolCallParser _parser, ILogger<ToolCallHandler> _logger) : IChunkHandler
{
    private readonly StringBuilder _argBuilder = new();
    private ToolCall? _firstTool;
    private string? _pendingToolId;
    private string? _pendingToolName;

    public StreamKind Kind => StreamKind.ToolCallDelta;

    public void OnRequest(LLMRequest request)
    {
        _firstTool = null;
        _pendingToolId = null;
        _pendingToolName = null;
        _argBuilder.Clear();
    }

    public void OnChunk(LLMStreamChunk chunk)
    {
        if (chunk.Kind != StreamKind.ToolCallDelta) return;

        var td = chunk.AsToolCallDelta();
        if (td == null || string.IsNullOrEmpty(td.Delta)) return;

        _logger.LogDebug("◄ Stream [ToolDelta]: Name={Name} Id={Id} Delta={Delta}", td.Name, td.Id, td.Delta);

        _pendingToolName ??= td.Name;
        _pendingToolId ??= td.Id ?? Guid.NewGuid().ToString();
        _argBuilder.Append(td.Delta);

        var raw = _argBuilder.ToString();
        if (!raw.TryParseCompleteJson(out var json)) return;

        if (_firstTool != null) throw new EarlyStopException("Second tool call detected.");
        if (!_tools.Contains(_pendingToolName!)) throw new RetryException($"{_pendingToolName}: invalid tool");

        _firstTool = new ToolCall(_pendingToolId!, _pendingToolName!, json!);
        _argBuilder.Clear();
        _pendingToolId = null;
        _pendingToolName = null;
    }

    public void OnResponse(LLMResponse response)
    {
        if (_firstTool == null) return;
        response.ToolCall = _parser.Validate(_firstTool);
        _logger.LogDebug("◄ Result [ToolCall]: Name={Name} Params={Params}", response.ToolCall.Name, response.ToolCall.Parameters.AsPrettyJson());
    }
}
