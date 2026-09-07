using System;
using System.IO;
using AgentCore.LLM.Chat;

namespace CodeSharp;

public sealed class CodeSharpTextBehavior(string? spilloverDirectory = null) : IContentBehavior<Text>
{
    public string? SpilloverDirectory { get; } = spilloverDirectory;

    public int EstimateTokens(Text content) => content.EstimateTokens();

    public Text Truncate(Text content, int maxTokens, string? notice = null)
    {
        if (content.EstimateTokens() <= maxTokens)
            return content;

        string? spillFilePath = TrySaveSpillover(content.Value);

        int totalLines = content.Value.AsSpan().Count('\n') + 1;
        notice ??= spillFilePath != null
            ? $"\n... [Output truncated (Total lines: {totalLines}). Full output saved to: {spillFilePath}. Use RunCommand with Get-Content to inspect.]"
            : $"\n... [Output truncated (Total lines: {totalLines})]";

        int noticeTokens = (int)Math.Ceiling(notice.Length / 4.0);
        int contentBudget = Math.Max(0, maxTokens - noticeTokens);
        int maxChars = contentBudget * 4;

        string head = maxChars < content.Value.Length ? content.Value[..maxChars] : content.Value;
        return new Text(head + notice);
    }

    private string? TrySaveSpillover(string fullContent)
    {
        if (string.IsNullOrWhiteSpace(SpilloverDirectory))
            return null;

        try
        {
            Directory.CreateDirectory(SpilloverDirectory);
            string fileName = $"output_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.log";
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
