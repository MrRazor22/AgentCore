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

public sealed class StreamProcessor(
    IToolCatalog _tools,
    IToolCallParser _parser,
    ITokenManager _tokenManager,
    ILogger<StreamProcessor> _logger)
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
    private FinishReason _finish = FinishReason.Stop;
    private string? _requestPayload;

    public void OnRequest(LLMRequest request)
    {
        _textBuffer.Clear();
        _toolBuffer.Clear();
        _jsonBuffer.Clear();
        _inlineTool = null;
        _toolCall = null;
        _pendingToolId = null;
        _pendingToolName = null;
        _usage = null;
        _finish = FinishReason.Stop;
        _requestPayload = request.ToCountablePayload();
        _outputType = request.OutputType;
        _schema = _outputType != null ? SchemaCache.GetOrAdd(_outputType, t => t.GetSchemaForType()) : null;

        if (_outputType != null)
            _logger.LogDebug("► Request [JsonSchema]: Type={Type}\n{Schema}", _outputType.Name, _schema!.ToJsonString(IndentedOptions));

        var last = request.Prompt.LastOrDefault();
        if (last != null)
            _logger.LogDebug("► Request [Prompt]: Role={Role} Content={Content}", last.Role, last.Content.AsPrettyJson());
    }

    public void OnChunk(LLMStreamChunk chunk)
    {
        switch (chunk.Kind)
        {
            case StreamKind.Text:
            case StreamKind.Structured:
                ProcessTextOrStructured(chunk);
                break;
            case StreamKind.ToolCallDelta:
                ProcessToolCallDelta(chunk);
                break;
            case StreamKind.Usage:
                _usage = chunk.AsTokenUsage();
                break;
            case StreamKind.Finish:
                _finish = chunk.AsFinishReason() ?? FinishReason.Stop;
                break;
        }
    }

    public void OnResponse(LLMResponse response)
    {
        response.FinishReason = _finish;

        if (_inlineTool != null)
        {
            response.ToolCall = _parser.Validate(_inlineTool);
            _logger.LogDebug("◄ Result [Inline ToolCall]: Name={Name}, Params={Params}",
                response.ToolCall.Name, response.ToolCall.Arguments.AsPrettyJson());
        }
        else if (_toolCall != null)
        {
            response.ToolCall = _parser.Validate(_toolCall);
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

        response.TokenUsage = _tokenManager.ResolveAndRecord(_requestPayload!, response.ToCountablePayload(), _usage);
    }

    private void ProcessTextOrStructured(LLMStreamChunk chunk)
    {
        var text = chunk.AsText();
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
                    throw new EarlyStopException("Inline tool call detected.");
                }
            }
        }
    }

    private void ProcessToolCallDelta(LLMStreamChunk chunk)
    {
        var td = chunk.AsToolCallDelta();
        if (td == null || string.IsNullOrEmpty(td.Delta)) return;

        _logger.LogDebug("◄ Stream [ToolDelta]: Name={Name} Id={Id} Delta={Delta}", td.Name, td.Id, td.Delta);

        _pendingToolName ??= td.Name;
        _pendingToolId ??= td.Id ?? Guid.NewGuid().ToString();
        _toolBuffer.Append(td.Delta);

        var raw = _toolBuffer.ToString();
        if (!raw.TryParseCompleteJson(out var json)) return;

        if (_toolCall != null) throw new EarlyStopException("Second tool call detected.");
        if (!_tools.Contains(_pendingToolName!)) throw new RetryException($"{_pendingToolName}: invalid tool");

        _toolCall = new ToolCall(_pendingToolId!, _pendingToolName!, json!);
        _toolBuffer.Clear();
        _pendingToolId = null;
        _pendingToolName = null;
    }

    private void ProcessStructuredOutput(LLMResponse response)
    {
        var raw = _jsonBuffer.ToString();
        if (string.IsNullOrWhiteSpace(raw)) throw new RetryException("Empty structured response");

        JsonNode? json;
        try { json = JsonNode.Parse(raw); }
        catch { throw new RetryException("Invalid JSON returned by model"); }

        var errors = _schema?.Validate(json, _outputType!.Name);
        if (errors?.Count > 0) throw new RetryException("Schema validation failed");

        response.Output = JsonSerializer.Deserialize(json!, _outputType!);
        _logger.LogDebug("Result [Json]: {Type}", json!.ToJsonString(IndentedOptions));
    }
}
