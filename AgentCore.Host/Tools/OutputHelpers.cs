namespace CodeSharp.Tools;

/// <summary>
/// Output formatting and truncation utilities for tool results.
/// </summary>
public static class OutputHelpers
{
    /// <summary>
    /// Truncates long text by keeping the beginning (head) and end (tail),
    /// which is ideal for build/test output where both configuration and errors matter.
    /// </summary>
    public static string HeadTail(string text, int maxChars = 20_000)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        int half = (maxChars - 100) / 2;
        var head = text[..half];
        var tail = text[^half..];
        int truncatedCount = text.Length - (half * 2);

        return $"{head}\n\n[... {truncatedCount:N0} characters truncated ...]\n\n{tail}";
    }

    /// <summary>
    /// Formats source code lines with line numbers.
    /// </summary>
    public static string FormatWithLineNumbers(IReadOnlyList<string> lines, int startLine = 1)
    {
        if (lines.Count == 0)
            return string.Empty;

        int maxLineNumWidth = (startLine + lines.Count - 1).ToString().Length;
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < lines.Count; i++)
        {
            int lineNum = startLine + i;
            sb.Append(lineNum.ToString().PadLeft(maxLineNumWidth))
              .Append(": ")
              .AppendLine(lines[i]);
        }

        return sb.ToString().TrimEnd();
    }
}
