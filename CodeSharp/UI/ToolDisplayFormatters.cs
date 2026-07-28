using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;

namespace CodeSharp.UI;

public sealed class CompositeToolDisplayFormatter : IToolDisplayFormatter
{
    private readonly List<IToolDisplayFormatter> _formatters = new();
    private readonly IToolDisplayFormatter _fallback = new GenericFallbackToolFormatter();

    public CompositeToolDisplayFormatter(IEnumerable<IToolDisplayFormatter> formatters)
    {
        if (formatters != null)
        {
            _formatters.AddRange(formatters);
        }
    }

    public bool CanFormat(string toolName) => true;

    public ToolDisplayInfo FormatCall(ToolCall call)
    {
        var formatter = _formatters.FirstOrDefault(f => f.CanFormat(call.Name));
        if (formatter != null)
        {
            return formatter.FormatCall(call);
        }
        return _fallback.FormatCall(call);
    }

    public string FormatResult(string rawResult)
    {
        // Handled by dispatching based on context where needed, or using a fallback if target matches.
        // For composite, we don't have the tool name during raw result formatting unless tracked.
        // However, in our system, we track callId -> toolName, so we can dispatch dynamically:
        return rawResult;
    }

    // Helper dispatch when toolName is known
    public string FormatResult(string toolName, string rawResult)
    {
        var formatter = _formatters.FirstOrDefault(f => f.CanFormat(toolName));
        if (formatter != null)
        {
            return formatter.FormatResult(rawResult);
        }
        return _fallback.FormatResult(rawResult);
    }
}

public sealed class RunCommandFormatter : IToolDisplayFormatter
{
    public bool CanFormat(string toolName) =>
        string.Equals(toolName, "RunCommand", StringComparison.OrdinalIgnoreCase);

    public ToolDisplayInfo FormatCall(ToolCall call)
    {
        call.Arguments.TryGetPropertyValue("commandLine", out var cmdNode);
        var cmd = cmdNode?.ToString() ?? "";

        call.Arguments.TryGetPropertyValue("cwd", out var cwdNode);
        var cwd = cwdNode?.ToString();
        var meta = !string.IsNullOrEmpty(cwd) ? $" in {cwd}" : "";

        return new ToolDisplayInfo(
            DisplayName: "Run command?",
            ArgSummary: cmd + meta
        );
    }

    public string FormatResult(string rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult)) return "(empty)";
        var firstLine = rawResult.Split('\n').FirstOrDefault()?.Trim() ?? "";
        return firstLine.Length > 120 ? firstLine[..120] + "..." : firstLine;
    }
}

public sealed class EditFileFormatter : IToolDisplayFormatter
{
    public bool CanFormat(string toolName) =>
        string.Equals(toolName, "EditFile", StringComparison.OrdinalIgnoreCase);

    public ToolDisplayInfo FormatCall(ToolCall call)
    {
        call.Arguments.TryGetPropertyValue("filePath", out var pathNode);
        var path = pathNode?.ToString() ?? "";

        call.Arguments.TryGetPropertyValue("targetContent", out var targetNode);
        var target = targetNode?.ToString();

        call.Arguments.TryGetPropertyValue("replacementContent", out var replacementNode);
        var replacement = replacementNode?.ToString() ?? "";

        bool isCreate = string.IsNullOrEmpty(target);
        string display = isCreate ? "Create file?" : "Edit file?";
        
        string sizeDesc = $"{(double)replacement.Length / 1024.0:F1} KB";
        string linesDesc = $"{replacement.Split('\n').Length} lines";
        string meta = isCreate 
            ? $"{sizeDesc} · {linesDesc}" 
            : $"{sizeDesc} · {linesDesc} · overwrite existing section";

        return new ToolDisplayInfo(
            DisplayName: display,
            ArgSummary: $"{path} ({meta})",
            LongDetails: replacement
        );
    }

    public string FormatResult(string rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult)) return "(empty)";
        var firstLine = rawResult.Split('\n').FirstOrDefault()?.Trim() ?? "";
        return firstLine.Length > 120 ? firstLine[..120] + "..." : firstLine;
    }
}

public sealed class FilesystemFormatter : IToolDisplayFormatter
{
    public bool CanFormat(string toolName) =>
        string.Equals(toolName, "Filesystem", StringComparison.OrdinalIgnoreCase);

    public ToolDisplayInfo FormatCall(ToolCall call)
    {
        call.Arguments.TryGetPropertyValue("action", out var actionNode);
        var action = actionNode?.ToString() ?? "";

        call.Arguments.TryGetPropertyValue("path", out var pathNode);
        var path = pathNode?.ToString() ?? "";

        call.Arguments.TryGetPropertyValue("destination", out var destNode);
        var dest = destNode?.ToString() ?? "";

        string display = action.ToLowerInvariant() switch
        {
            "delete" => "Delete file?",
            "move" => "Move file?",
            "copy" => "Copy file?",
            _ => $"{char.ToUpperInvariant(action[0])}{action[1..]} file?"
        };

        string target = string.IsNullOrEmpty(dest) ? path : $"{path} -> {dest}";

        return new ToolDisplayInfo(
            DisplayName: display,
            ArgSummary: target
        );
    }

    public string FormatResult(string rawResult)
    {
        return rawResult.Length > 120 ? rawResult[..120] + "..." : rawResult;
    }
}

public sealed class SearchToolFormatter : IToolDisplayFormatter
{
    public bool CanFormat(string toolName) =>
        string.Equals(toolName, "Search", StringComparison.OrdinalIgnoreCase);

    public ToolDisplayInfo FormatCall(ToolCall call)
    {
        call.Arguments.TryGetPropertyValue("path", out var pathNode);
        var path = pathNode?.ToString() ?? ".";

        call.Arguments.TryGetPropertyValue("query", out var queryNode);
        var query = queryNode?.ToString() ?? "";

        var summary = string.IsNullOrEmpty(query) ? $"path: {path}" : $"path: {path} | query: {query}";

        return new ToolDisplayInfo(
            DisplayName: "Search",
            ArgSummary: summary
        );
    }

    public string FormatResult(string rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult)) return "(empty)";
        var lines = rawResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Summarize items/matches
        if (lines.Length == 1)
        {
            var first = lines[0].Trim();
            return first.Length > 120 ? first[..120] + "..." : first;
        }

        return $"{lines.Length} items found";
    }
}

public sealed class SearchWebFormatter : IToolDisplayFormatter
{
    public bool CanFormat(string toolName) =>
        string.Equals(toolName, "SearchWeb", StringComparison.OrdinalIgnoreCase);

    public ToolDisplayInfo FormatCall(ToolCall call)
    {
        call.Arguments.TryGetPropertyValue("query", out var queryNode);
        var query = queryNode?.ToString() ?? "";

        return new ToolDisplayInfo(
            DisplayName: "SearchWeb",
            ArgSummary: query
        );
    }

    public string FormatResult(string rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult)) return "(empty)";
        var lines = rawResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 1)
        {
            var first = lines[0].Trim();
            return first.Length > 120 ? first[..120] + "..." : first;
        }
        return $"{lines.Length} lines of web results";
    }
}

public sealed class GenericFallbackToolFormatter : IToolDisplayFormatter
{
    public bool CanFormat(string toolName) => true;

    public ToolDisplayInfo FormatCall(ToolCall call)
    {
        var summaries = new List<string>();
        foreach (var prop in call.Arguments)
        {
            summaries.Add($"{prop.Key}: {prop.Value?.ToString()}");
        }
        
        var summary = summaries.Count > 0 ? string.Join(" | ", summaries) : "";
        var rawArgs = "";
        try
        {
            rawArgs = JsonSerializer.Serialize(call.Arguments, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            rawArgs = call.Arguments?.ToString() ?? "{}";
        }

        return new ToolDisplayInfo(
            DisplayName: $"Execute tool '{call.Name}'?",
            ArgSummary: summary,
            LongDetails: rawArgs
        );
    }

    public string FormatResult(string rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult)) return "(empty)";
        var first = rawResult.Split('\n').FirstOrDefault()?.Trim() ?? "";
        return first.Length > 120 ? first[..120] + "..." : first;
    }
}
