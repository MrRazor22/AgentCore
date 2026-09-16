using System.ComponentModel;
using System.Threading.Tasks;
using AgentCore.Host.Abstractions;
using AgentCore.Tooling.Tools;

namespace AgentCore.Host;

public sealed class VsStudioTools(IVsToolDispatcher dispatcher)
{
    [Tool("ReadFile", "Read lines from a text file within a specified line range.")]
    public Task<string> ReadFile(
        [Description("Path to file relative to workspace root.")] string filePath,
        [Description("Starting line (1-based, default 1).")] int startLine = 1,
        [Description("Ending line (inclusive, default 800 max).")] int endLine = 0) =>
        dispatcher.InvokeAsync("ReadFile", new { filePath, startLine, endLine });

    [Tool("EditFile", "Edit an existing file by replacing exact target text, or create a new file.")]
    public Task<string> EditFile(
        [Description("Path to file relative to workspace root.")] string filePath,
        [Description("Target text to replace. Leave empty for new file.")] string targetContent = "",
        [Description("Replacement content.")] string replacementContent = "",
        [Description("Advisory risk assessment flag.")] bool safeToAutoRun = false) =>
        dispatcher.InvokeAsync("EditFile", new { filePath, targetContent, replacementContent, safeToAutoRun });

    [Tool("Search", "Find files or search exact text / regex patterns within the workspace.")]
    public Task<string> Search(
        [Description("Directory or file to search relative to workspace root.")] string? path = null,
        [Description("Text or pattern to search. If omitted, lists directory tree.")] string? query = null,
        [Description("Glob pattern to filter files.")] string? include = null,
        [Description("Treat query as regular expression if true.")] bool isRegex = false,
        [Description("Case sensitive matching if true.")] bool caseSensitive = false) =>
        dispatcher.InvokeAsync("Search", new { path, query, include, isRegex, caseSensitive });

    [Tool("RunCommand", "Execute PowerShell commands within the Visual Studio environment.")]
    public Task<string> RunCommand(
        [Description("PowerShell command line.")] string? commandLine = null,
        [Description("CommandId of a background process.")] string? commandId = null,
        [Description("Working directory.")] string? cwd = null,
        [Description("Run independently in background.")] bool background = false,
        [Description("Send notification on completion.")] bool notifyOnCompletion = true,
        [Description("Max output character count.")] int outputCharacterCount = 20_000,
        [Description("Advisory risk assessment flag.")] bool safeToAutoRun = false) =>
        dispatcher.InvokeAsync("RunCommand", new { commandLine, commandId, cwd, background, notifyOnCompletion, outputCharacterCount, safeToAutoRun });

    [Tool("TodoList", "Set or update the agent session todo list checklist.")]
    public Task<string> TodoList(
        [Description("List of task items.")] string[]? todos = null) =>
        dispatcher.InvokeAsync("TodoList", new { todos });
}
