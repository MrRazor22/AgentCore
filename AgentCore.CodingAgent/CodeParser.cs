using System.Text;
using System.Text.RegularExpressions;

namespace AgentCore.CodingAgent;

public static class CodeParser
{
    private static readonly Regex CsharpFenceRegex = new(
        @"```csharp\s*(.*?)```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CodeTagRegex = new(
        @"<code>(.*?)</code>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static string ParseCodeBlock(string text, (string open, string close) tags)
    {
        var match = CsharpFenceRegex.Match(text);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        if (tags.open == "<code>")
        {
            match = CodeTagRegex.Match(text);
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        if (IsValidCSharp(text))
            return text;

        throw new CodeParseException(
            $"Your code snippet is invalid, because no valid code block was found.\n" +
            $"Make sure to include code with the correct pattern, for instance:\n" +
            $"Thought: Your thoughts\n" +
            $"```\n" +
            $"# Your C# code here\n" +
            $"```");
    }

    private static bool IsValidCSharp(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        try
        {
            var tree = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseSyntaxTree(code);
            return tree.GetDiagnostics().All(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        }
        catch
        {
            return false;
        }
    }
}

public class CodeParseException : Exception
{
    public CodeParseException(string message) : base(message) { }
}
