using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using VisualStudioAgent.Abstractions;

namespace VisualStudioAgent.Services;

public sealed class VsAdapter(
    IVsEditorService editor,
    IVsSearchService search,
    IVsTerminalService terminal,
    ITodoManager todo) : IVsAdapter
{
    public async Task<(string Result, bool IsError)> DispatchAsync(string tool, JsonElement args)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = (DTE?)Package.GetGlobalService(typeof(DTE));
        string slnDir = (!string.IsNullOrEmpty(dte?.Solution?.FullName))
            ? Path.GetDirectoryName(dte!.Solution.FullName) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;

        try
        {
            return tool.ToLowerInvariant() switch
            {
                "readfile" => (await editor.ReadFileAsync(
                    ResolvePath(args.GetProperty("filePath").GetString()!, slnDir),
                    args.TryGetProperty("startLine", out var s) ? s.GetInt32() : 1,
                    args.TryGetProperty("endLine", out var e) ? e.GetInt32() : 0), false),

                "editfile" => (await editor.EditFileAsync(
                    ResolvePath(args.GetProperty("filePath").GetString()!, slnDir),
                    args.TryGetProperty("targetContent", out var t) ? t.GetString() ?? "" : "",
                    args.TryGetProperty("replacementContent", out var r) ? r.GetString() ?? "" : ""), false),

                "search" => (await search.SearchAsync(
                    args.TryGetProperty("path", out var p) && !string.IsNullOrWhiteSpace(p.GetString()) ? ResolvePath(p.GetString()!, slnDir) : slnDir,
                    args.TryGetProperty("query", out var q) ? q.GetString() : null,
                    args.TryGetProperty("include", out var inc) ? inc.GetString() : null,
                    args.TryGetProperty("isRegex", out var ir) && ir.GetBoolean(),
                    args.TryGetProperty("caseSensitive", out var cs) && cs.GetBoolean()), false),

                "runcommand" => await terminal.RunCommandAsync(
                    args.TryGetProperty("commandLine", out var c) ? c.GetString() ?? "" : "",
                    args.TryGetProperty("cwd", out var cw) && !string.IsNullOrWhiteSpace(cw.GetString()) ? ResolvePath(cw.GetString()!, slnDir) : slnDir,
                    args.TryGetProperty("outputCharacterCount", out var m) ? m.GetInt32() : 20_000),

                "todolist" => (todo.Update(args.TryGetProperty("todos", out var tProp) && tProp.ValueKind == JsonValueKind.Array
                    ? tProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                    : null), false),

                _ => ($"Unknown tool: '{tool}'", true)
            };
        }
        catch (Exception ex)
        {
            return ($"Error: {ex.Message}", true);
        }
    }

    private static string ResolvePath(string path, string slnDir) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(slnDir, path));
}
