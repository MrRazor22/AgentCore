using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VisualStudioAgent.Abstractions;

namespace VisualStudioAgent.Services;

public sealed class VsSearchService : IVsSearchService
{
    public Task<string> SearchAsync(string? targetPath, string? query, string? include, bool isRegex, bool caseSensitive)
    {
        string path = targetPath ?? Environment.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(ListDirectoryEntries(path));
        return Task.FromResult(GrepSearch(path, query, include, isRegex, caseSensitive));
    }

    private static string ListDirectoryEntries(string targetPath)
    {
        if (File.Exists(targetPath)) return $"File: {Path.GetFileName(targetPath)} ({new FileInfo(targetPath).Length} bytes)";
        if (!Directory.Exists(targetPath)) return $"Error: Directory '{targetPath}' not found.";

        var entries = Directory.EnumerateFileSystemEntries(targetPath)
            .Where(e => !e.Contains("\\.git") && !e.Contains("\\bin") && !e.Contains("\\obj") && !e.Contains("\\.vs"))
            .Take(100)
            .Select(e => Directory.Exists(e) ? $"[DIR]  {Path.GetFileName(e)}" : $"[FILE] {Path.GetFileName(e)} ({new FileInfo(e).Length} bytes)");

        return string.Join("\n", entries);
    }

    private static string GrepSearch(string targetPath, string query, string? include, bool isRegex, bool caseSensitive)
    {
        var matches = new List<string>();
        var files = Directory.EnumerateFiles(targetPath, include ?? "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\.vs\\"));

        var regex = isRegex ? new Regex(query, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) : null;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var file in files)
        {
            if (matches.Count >= 50) break;
            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    bool matched = isRegex ? regex!.IsMatch(lines[i]) : lines[i].IndexOf(query, comparison) >= 0;
                    if (matched)
                    {
                        string rel = file.StartsWith(targetPath, StringComparison.OrdinalIgnoreCase)
                            ? file.Substring(targetPath.Length).TrimStart('\\', '/')
                            : file;
                        matches.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
                        if (matches.Count >= 50) break;
                    }
                }
            }
            catch { }
        }

        return matches.Count == 0 ? "No matches found." : string.Join("\n", matches);
    }
}
