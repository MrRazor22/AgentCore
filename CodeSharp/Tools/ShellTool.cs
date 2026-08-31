using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Tools;
using CodeSharp.Security;

namespace CodeSharp.Tools;

/// <summary>
/// Hardened, sandboxed shell tool executing PowerShell inside an isolated Docker container.
/// </summary>
public sealed class ShellTool
{
    private readonly string _workspaceRoot;
    private readonly int _maxOutputChars;
    private readonly DockerSandbox _sandbox;
    private readonly ConcurrentDictionary<string, BackgroundCommandTracker> _backgroundTasks = new();

    public ShellTool(
        string workspaceRoot,
        int maxOutputChars = 20_000,
        string image = "mcr.microsoft.com/powershell:lts-alpine")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _maxOutputChars = maxOutputChars;
        _sandbox = new DockerSandbox(_workspaceRoot, image: image);
    }

    [Tool("RunCommand", "Execute sandboxed PowerShell commands in an isolated container or inspect background task status.")]
    public async Task<string> RunCommand(
        [Description("PowerShell command line to execute (for new execution).")] string? commandLine = null,
        [Description("CommandId of a background process to check status for (for status inspection).")] string? commandId = null,
        [Description("Working directory relative to workspace root.")] string? cwd = null,
        [Description("Run independently in background if true.")] bool background = false,
        [Description("Send notification on completion if background is true.")] bool notifyOnCompletion = true,
        [Description("Max output character count to return.")] int outputCharacterCount = 20_000,
        [Description("Advisory risk assessment flag.")] bool safeToAutoRun = false,
        CancellationToken ct = default)
    {
        // Inspection mode
        if (string.IsNullOrWhiteSpace(commandLine) && !string.IsNullOrWhiteSpace(commandId))
        {
            return GetCommandStatus(commandId, outputCharacterCount);
        }

        if (string.IsNullOrWhiteSpace(commandLine))
            return "Error: Either commandLine (to execute) or commandId (to inspect status) must be provided.";

        var targetCwd = string.IsNullOrWhiteSpace(cwd)
            ? _workspaceRoot
            : PathGuard.EnsureWithinWorkspace(_workspaceRoot, cwd);

        var id = commandId ?? "cmd_" + Guid.NewGuid().ToString("N")[..8];

        if (background)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var task = Task.Run(async () =>
            {
                try
                {
                    return await _sandbox.ExecuteAsync(commandLine, targetCwd, ct: cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new SandboxExecutionResult(-1, string.Empty, ex.Message);
                }
            }, cts.Token);

            var tracker = new BackgroundCommandTracker(id, commandLine, task, cts);
            _backgroundTasks[id] = tracker;

            return $"Background process launched successfully in Docker sandbox.\nCommandId: {id}\nStatus: Running";
        }

        // Foreground execution
        var result = await _sandbox.ExecuteAsync(commandLine, targetCwd, ct: ct).ConfigureAwait(false);

        var combined = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : (string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError
                : $"{result.StandardOutput}\n[STDERR]\n{result.StandardError}");

        var formatted = OutputHelpers.HeadTail(combined, Math.Min(outputCharacterCount, _maxOutputChars));
        return $"Command completed with exit code {result.ExitCode}.\n\nOutput:\n{formatted}";
    }

    private string GetCommandStatus(string commandId, int maxChars)
    {
        if (!_backgroundTasks.TryGetValue(commandId, out var tracker))
            return $"Error: CommandId '{commandId}' not found.";

        if (!tracker.Task.IsCompleted)
        {
            return $"CommandId: {commandId}\nStatus: Running";
        }

        var result = tracker.Task.GetAwaiter().GetResult();
        var combined = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : $"{result.StandardOutput}\n[STDERR]\n{result.StandardError}";

        var formatted = OutputHelpers.HeadTail(combined, Math.Min(maxChars, _maxOutputChars));
        return $"CommandId: {commandId}\nStatus: Completed (Exit code: {result.ExitCode})\n\nOutput:\n{formatted}";
    }

    private sealed record BackgroundCommandTracker(
        string Id,
        string CommandLine,
        Task<SandboxExecutionResult> Task,
        CancellationTokenSource Cts
    );
}
