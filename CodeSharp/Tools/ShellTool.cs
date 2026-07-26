using System.ComponentModel;
using System.Diagnostics;
using AgentCore.Tools;
using CodeSharp.Security;

namespace CodeSharp.Tools;

/// <summary>
/// Tool for managing process execution, background tasks, and command status inspection.
/// </summary>
public sealed class ShellTool
{
    private readonly string _workspaceRoot;
    private readonly int _maxOutputChars;
    private readonly Dictionary<string, ProcessTracker> _processes = new();
    private readonly object _lock = new();

    public ShellTool(string workspaceRoot, int maxOutputChars = 20_000)
    {
        _workspaceRoot = workspaceRoot;
        _maxOutputChars = maxOutputChars;
    }

    [Tool("RunCommand", "Execute PowerShell commands or inspect background process status.")]
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
        // Path inspection mode vs launch mode
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

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{commandLine.Replace("\"", "\\\"")}\"",
            WorkingDirectory = targetCwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var tracker = new ProcessTracker(id, commandLine, psi);

        lock (_lock)
        {
            _processes[id] = tracker;
        }

        tracker.Start();

        if (background)
        {
            return $"Background process launched successfully.\nCommandId: {id}\nStatus: Running";
        }

        // Foreground execution
        try
        {
            await tracker.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                tracker.Kill();
            }
            catch
            {
                // Silence process clean-up errors on cancellation exit
            }
            throw;
        }

        var output = tracker.GetCombinedOutput();
        var formatted = OutputHelpers.HeadTail(output, Math.Min(outputCharacterCount, _maxOutputChars));

        return $"Command completed with exit code {tracker.ExitCode}.\n\nOutput:\n{formatted}";
    }

    private string GetCommandStatus(string commandId, int maxChars)
    {
        ProcessTracker? tracker;
        lock (_lock)
        {
            _processes.TryGetValue(commandId, out tracker);
        }

        if (tracker == null)
            return $"Error: CommandId '{commandId}' not found.";

        var status = tracker.IsCompleted ? $"Completed (Exit code: {tracker.ExitCode})" : "Running";
        var output = tracker.GetCombinedOutput();
        var formatted = OutputHelpers.HeadTail(output, Math.Min(maxChars, _maxOutputChars));

        return $"CommandId: {commandId}\nStatus: {status}\n\nOutput:\n{formatted}";
    }

    private sealed class ProcessTracker
    {
        public string Id { get; }
        public string CommandLine { get; }
        private readonly ProcessStartInfo _psi;
        private Process? _process;
        private readonly System.Text.StringBuilder _output = new();
        private readonly object _lock = new();

        public bool IsCompleted => _process?.HasExited ?? false;
        public int ExitCode => _process?.HasExited == true ? _process.ExitCode : -1;

        public ProcessTracker(string id, string commandLine, ProcessStartInfo psi)
        {
            Id = id;
            CommandLine = commandLine;
            _psi = psi;
        }

        public void Start()
        {
            _process = new Process { StartInfo = _psi };
            _process.OutputDataReceived += (s, e) => AppendOutput(e.Data);
            _process.ErrorDataReceived += (s, e) => AppendOutput(e.Data);

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public async Task WaitForExitAsync(CancellationToken ct)
        {
            if (_process != null)
            {
                await _process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }

        public void Kill()
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }

        private void AppendOutput(string? data)
        {
            if (data == null) return;
            lock (_lock)
            {
                _output.AppendLine(data);
            }
        }

        public string GetCombinedOutput()
        {
            lock (_lock)
            {
                return _output.ToString();
            }
        }
    }
}
