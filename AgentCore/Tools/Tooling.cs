using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;

namespace AgentCore.Tools;

public interface ITooling
{
    IReadOnlyList<ToolDefinition> GetDefinitions();
    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default);
}

internal sealed class Tooling : ITooling
{
    private readonly IReadOnlyList<ToolDefinition> _toolDefinitions;
    private readonly IReadOnlyDictionary<string, Tool> _tools;
    private readonly ILogger<Tooling> _logger;

    public Tooling(
        IReadOnlyList<Tool> tools,
        ILogger<Tooling>? logger = null)
    {
        var toolList = tools ?? Array.Empty<Tool>();

        var duplicates = toolList
            .GroupBy(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new ArgumentException($"Duplicate tool names registered: {string.Join(", ", duplicates)}");
        }

        _toolDefinitions = toolList.Select(t => t.Definition).ToList();
        _tools = toolList.ToDictionary(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger ?? NullLogger<Tooling>.Instance;
    }

    public IReadOnlyList<ToolDefinition> GetDefinitions() => _toolDefinitions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(call.Name))
        {
            _logger.LogWarning("Tool validation failed. Reason='Tool name empty'");
            return Failed(call.Id, "Unknown", "Tool name cannot be empty.");
        }

        _tools.TryGetValue(call.Name, out var tool);
        if (tool == null)
        {
            _logger.LogWarning("Tool validation failed. ToolName={ToolName}, Reason='Not registered'", call.Name);
            return Failed(call.Id, call.Name, $"Tool '{call.Name}' not registered.");
        }

        var errors = tool.Definition.ParametersSchema.Validate(call.Arguments);
        if (errors.Any())
        {
            var errorMessage = string.Join("; ", errors);
            _logger.LogWarning("Tool validation failed. ToolName={ToolName}, Error={Error}", call.Name, errorMessage);
            return Failed(call.Id, call.Name, errorMessage);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var rawResult = await tool.InvokeAsync(call.Arguments, ct).ConfigureAwait(false);
            IContent result = rawResult switch
            {
                IContent c => c,
                null => new Text(string.Empty),
                string s => new Text(s),
                Exception ex => new Text(ex.Message),
                _ => new Text(JsonSerializer.Serialize(rawResult))
            };

            sw.Stop();
            _logger.LogInformation("Tool executed. ToolName={ToolName}, DurationMs={DurationMs}", call.Name, sw.ElapsedMilliseconds);
            return new ToolResult(call.Id, [result]);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            var actualEx = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                ? tie.InnerException
                : ex;

            _logger.LogError(actualEx, "Tool execution failed. ToolName={ToolName}, DurationMs={DurationMs}, Error={Message}", call.Name, sw.ElapsedMilliseconds, actualEx.Message);
            return Failed(call.Id, call.Name, actualEx.Message);
        }
    }

    private static ToolResult Failed(string callId, string toolName, string message)
        => new(callId, [new Text($"Error calling tool '{toolName}': {message}")]);
}
