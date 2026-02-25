using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Execution;

public sealed class ResponseAssembler(
    IToolCatalog _tools,
    IToolCallParser _parser,
    ITokenManager _tokenManager,
    ILogger<ResponseAssembler> _logger)
{
    private static readonly ConcurrentDictionary<Type, JsonObject> SchemaCache = new();
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    private readonly StringBuilder _textBuffer = new();
    private readonly StringBuilder _toolBuffer = new();
    private readonly StringBuilder _jsonBuffer = new();

    private Type? _outputType;
    private JsonObject? _schema;
    private ToolCall? _inlineTool;
    private ToolCall? _toolCall;
    private string? _pendingToolId;
    private string? _pendingToolName;
    private TokenUsage? _usage;
    private Protocol.FinishReason _finish = Protocol.FinishReason.Stop;
    private bool _hasValidToolCall;

    public void Reset(Type? outputType)
    {
        _textBuffer.Clear();
        _toolBuffer.Clear();
        _jsonBuffer.Clear();
        _inlineTool = null;
        _toolCall = null;
        _pendingToolId = null;
        _pendingToolName = null;
        _usage = null;
        _finish = Protocol.FinishReason.Stop;
        _hasValidToolCall = false;
        _outputType = outputType;
        _schema = _outputType != null ? SchemaCache.GetOrAdd(_outputType, t => t.GetSchemaForType()) : null;

        if (_outputType != null)
            _logger.LogDebug("► Request [JsonSchema]: Type={Type}\n{Schema}", _outputType.Name, _schema!.ToJsonString(IndentedOptions));
    }

    public void OnDelta(AgentCore.Chat.IContentDelta delta)
    {
        if (_hasValidToolCall) return;

        switch (delta)
        {
            case AgentCore.Chat.TextDelta t:
                ProcessText(t.Value);
                break;

            case AgentCore.Chat.ToolCallDelta tc:
                ProcessToolCall(tc);
                break;
        }
    }

    public void SetFinishReason(Protocol.FinishReason finish)
    {
        _finish = finish;
    }

    public void SetUsage(TokenUsage usage)
    {
        _usage = usage;
    }

    public Protocol.LLMResponse Build()
    {
        var response = new Protocol.LLMResponse();

        if (_inlineTool != null)
        {
            response.ToolCall = _inlineTool;
            _logger.LogDebug("◄ Result [Inline ToolCall]: Name={Name}, Params={Params}",
                response.ToolCall.Name, response.ToolCall.Arguments.AsPrettyJson());
        }
        else if (_toolCall != null)
        {
            response.ToolCall = _toolCall;
            _logger.LogDebug("◄ Result [ToolCall]: Name={Name} Params={Params}",
                response.ToolCall.Name, response.ToolCall.Arguments.AsPrettyJson());
        }
        else if (_outputType != null)
        {
            ProcessStructuredOutput(response);
        }
        else
        {
            var text = _textBuffer.ToString().Trim();
            response.Text = text;
            _logger.LogDebug("◄ Result [Text]: {Result}", text);
        }

        response.FinishReason = _finish;
        response.TokenUsage = _usage;

        return response;
    }

    private void ProcessText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_outputType != null)
        {
            _jsonBuffer.Append(text);
            _logger.LogDebug("◄ Stream [Json]: {Text}", text);
        }
        else
        {
            _textBuffer.Append(text);
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogDebug("◄ Stream [Text]: {Text}", text);

            if (_inlineTool == null)
            {
                var match = _parser.TryMatch(_textBuffer.ToString());
                if (match != null)
                {
                    _inlineTool = match;
                    _hasValidToolCall = true;
                }
            }
        }
    }

    private void ProcessToolCall(AgentCore.Chat.ToolCallDelta tc)
    {
        if (string.IsNullOrEmpty(tc.ArgumentsDelta)) return;

        _logger.LogDebug("◄ Stream [ToolDelta]: Name={Name} Id={Id} Delta={Delta}", tc.Name, tc.Id, tc.ArgumentsDelta);

        _pendingToolName ??= tc.Name;
        _pendingToolId ??= tc.Id ?? Guid.NewGuid().ToString();
        _toolBuffer.Append(tc.ArgumentsDelta);

        var raw = _toolBuffer.ToString();
        if (!raw.TryParseCompleteJson(out var json)) return;

        if (_toolCall != null)
        {
            _hasValidToolCall = true;
            return;
        }

        if (!_tools.Contains(_pendingToolName!))
        {
            _hasValidToolCall = true;
            return;
        }

        _toolCall = new ToolCall(_pendingToolId!, _pendingToolName!, json!);
        _toolBuffer.Clear();
        _pendingToolId = null;
        _pendingToolName = null;
        _hasValidToolCall = true;
    }

    private void ProcessStructuredOutput(Protocol.LLMResponse response)
    {
        var raw = _jsonBuffer.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            response.Error = "Empty structured response";
            return;
        }

        JsonNode? json;
        try { json = JsonNode.Parse(raw); }
        catch
        {
            response.Error = "Invalid JSON returned by model";
            return;
        }

        var errors = _schema?.Validate(json, _outputType!.Name);
        if (errors?.Count > 0)
        {
            response.Error = $"Schema validation failed: {string.Join("; ", errors.Select(e => e.ToString()))}";
            return;
        }

        response.Output = JsonSerializer.Deserialize(json!, _outputType!);
        _logger.LogDebug("Result [Json]: {Type}", json!.ToJsonString(IndentedOptions));
    }
}
