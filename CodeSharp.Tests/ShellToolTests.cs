using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CodeSharp.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class ShellToolTests : IDisposable
{
    private readonly string _tempWorkspace;

    public ShellToolTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "ShellToolTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempWorkspace);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task RunCommand_ExecutionBoundary_ThrowsSecurityExceptionOnEscape()
    {
        var tool = new ShellTool(_tempWorkspace);
        
        await Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await tool.RunCommand(commandLine: "echo 1", cwd: "../outside_dir");
        });
    }

    [Fact]
    public async Task RunCommand_ForegroundDeterministicRun_Succeeds()
    {
        var tool = new ShellTool(_tempWorkspace);
        var result = await tool.RunCommand(commandLine: "echo 'hello test'");

        Assert.Contains("Command completed with exit code 0", result);
        Assert.Contains("hello test", result);
    }

    [Fact]
    public async Task RunCommand_BackgroundAndStatus_ChecksCorrectly()
    {
        var tool = new ShellTool(_tempWorkspace);
        var launchResult = await tool.RunCommand(commandLine: "Start-Sleep -Seconds 1; echo 'bg-finished'", background: true);

        Assert.Contains("Background process launched successfully", launchResult);
        Assert.Contains("CommandId: ", launchResult);

        // Extract CommandId
        var lines = launchResult.Split('\n');
        var idLine = Array.Find(lines, l => l.StartsWith("CommandId:"));
        var commandId = idLine!.Replace("CommandId:", "").Trim();

        // Check running status
        var runningStatus = await tool.RunCommand(commandId: commandId);
        Assert.Contains("Status: Running", runningStatus);

        // Wait for exit
        string completedStatus = "";
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(250);
            completedStatus = await tool.RunCommand(commandId: commandId);
            if (completedStatus.Contains("Status: Completed"))
                break;
        }

        // Check completed status
        Assert.Contains("Status: Completed (Exit code: 0)", completedStatus);
        Assert.Contains("bg-finished", completedStatus);
    }

    [Fact]
    public async Task RunCommand_Cancellation_KillsProcessAndThrowsOperationCanceledException()
    {
        var tool = new ShellTool(_tempWorkspace);
        using var cts = new CancellationTokenSource();

        var runTask = tool.RunCommand(commandLine: "Start-Sleep -Seconds 10", ct: cts.Token);
        
        // Cancel shortly after starting
        await Task.Delay(200);
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await runTask;
        });
    }
}
