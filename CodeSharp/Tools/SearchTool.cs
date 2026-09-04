using System.ComponentModel;
using AgentCore.Tools;
using CodeSharp.Security;

namespace CodeSharp.Tools;

/// <summary>
/// Tool for file searching and directory content discovery.
/// </summary>
public static class SearchTool
{
    private static string _workspaceRoot = Environment.CurrentDirectory;

    public static void Initialize(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    [Tool("Search", "Find files or search exact text / regex patterns within the workspace.")]
    public static string Search(
        [Description("Directory or file path to search inside, relative to the workspace root. Use standard relative paths such as 'AgentCore/Tools'. Do not use URI schemes or prefixes such as 'solutionrelative:'.")] string? path = null,
        [Description("Text or pattern to search. If omitted, lists directory tree.")] string? query = null,
        [Description("Glob pattern to filter files (e.g. '*.cs').")] string? include = null,
        [Description("Treat query as regular expression if true.")] bool isRegex = false,
        [Description("Case sensitive matching if true.")] bool caseSensitive = false)
    {
        var targetPath = string.IsNullOrWhiteSpace(path)
            ? _workspaceRoot
            : PathGuard.EnsureWithinWorkspace(_workspaceRoot, path);

        // Mode 1: Directory listing when query is null/empty
        if (string.IsNullOrWhiteSpace(query))
        {
            if (File.Exists(targetPath))
                return $"File: {Path.GetFileName(targetPath)} ({new FileInfo(targetPath).Length} bytes)";

            if (!Directory.Exists(targetPath))
                return $"Error: Directory '{path}' not found.";

            var entries = Directory.EnumerateFileSystemEntries(targetPath)
                .Take(100)
                .Select(e => Directory.Exists(e) ? $"[DIR]  {Path.GetFileName(e)}" : $"[FILE] {Path.GetFileName(e)} ({new FileInfo(e).Length} bytes)");

            return string.Join("\n", entries);
        }

        // Mode 2: Search text content in files
        var matches = new List<string>();
        var searchOption = SearchOption.AllDirectories;

        var files = Directory.EnumerateFiles(targetPath, include ?? "*", searchOption)
            .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"));

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var file in files)
        {
            if (matches.Count >= 50) break;

            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (matches.Count >= 50) break;

                    bool isMatch = isRegex
                        ? System.Text.RegularExpressions.Regex.IsMatch(lines[i], query, caseSensitive ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        : lines[i].Contains(query, comparison);

                    if (isMatch)
                    {
                        var relPath = Path.GetRelativePath(_workspaceRoot, file);
                        matches.Add($"{relPath}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
            catch
            {
                // Ignore unreadable/binary files
            }
        }

        if (matches.Count == 0)
            return $"No matches found for '{query}'.";

        var result = string.Join("\n", matches);
        if (matches.Count >= 50)
            result += "\n\n[Results capped at 50 matches.]";

        return result;
    }
}
