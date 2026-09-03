using System;
using System.IO;
using System.Text.Json.Serialization;
using AgentCore.LLM.Chat;

namespace CodeSharp;

public record CodeSharpText(string Value, string? SpilloverDirectory = null) : Text(Value)
{
    public override string ToString() => Value;

    public override IContent Truncate(int maxTokens, string? notice = null)
    {
        if (EstimateTokens() <= maxTokens)
            return this;

        string? spillFilePath = TrySaveSpillover(Value);

        int totalLines = Value.AsSpan().Count('\n') + 1;
        notice ??= spillFilePath != null
            ? $"\n... [Output truncated (Total lines: {totalLines}). Full output saved to: {spillFilePath}. Use RunCommand with Get-Content to inspect.]"
            : $"\n... [Output truncated (Total lines: {totalLines})]";

        int noticeTokens = (int)Math.Ceiling(notice.Length / 4.0);
        int contentBudget = Math.Max(0, maxTokens - noticeTokens);
        int maxChars = contentBudget * 4;

        string head = maxChars < Value.Length ? Value[..maxChars] : Value;
        return new CodeSharpText(head + notice, SpilloverDirectory);
    }

    private string? TrySaveSpillover(string fullContent)
    {
        if (string.IsNullOrWhiteSpace(SpilloverDirectory))
            return null;

        try
        {
            Directory.CreateDirectory(SpilloverDirectory);
            string fileName = $"output_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.log";
            string fullPath = Path.Combine(SpilloverDirectory, fileName);
            File.WriteAllText(fullPath, fullContent);
            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
