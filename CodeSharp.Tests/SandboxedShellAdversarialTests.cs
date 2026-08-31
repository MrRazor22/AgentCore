using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CodeSharp.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class SandboxedShellAdversarialTests : IDisposable
{
    private readonly string _tempWorkspace;
    private readonly ShellTool _tool;

    public SandboxedShellAdversarialTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "DockerAdvTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempWorkspace);
        _tool = new ShellTool(_tempWorkspace);
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
    public async Task Adversarial_WorkspaceAllowed_ReadWriteSucceeds()
    {
        var result = await _tool.RunCommand(commandLine: "Set-Content -Path 'allowed.txt' -Value 'Hello Docker Sandbox'; Get-Content -Path 'allowed.txt'");

        Assert.Contains("Hello Docker Sandbox", result);
        Assert.Contains("exit code 0", result);
    }

    [Fact]
    public async Task Adversarial_FilesystemEscape_HostCredentialsAndSystemInaccessible()
    {
        // Inside Linux container, Windows paths like C:\Users or host .ssh do not exist
        var result = await _tool.RunCommand(commandLine: "Test-Path '/root/.ssh'; Test-Path 'C:/Users'");

        Assert.Contains("False", result);
        Assert.DoesNotContain("True", result);
    }

    [Fact]
    public async Task Adversarial_NetworkEscape_OutboundHttp_BlockedByContainerNetworkIsolation()
    {
        // Container has --network none, all outbound sockets must fail
        var result = await _tool.RunCommand(commandLine: "try { $client = [System.Net.Sockets.TcpClient]::new(); $client.Connect('1.1.1.1', 80); 'CONNECTED' } catch { 'NETWORK_BLOCKED' }");

        Assert.Contains("NETWORK_BLOCKED", result);
        Assert.DoesNotContain("CONNECTED", result);
    }

    [Fact]
    public async Task Adversarial_PathGuard_RelativeEscapeCwd_ThrowsSecurityException()
    {
        await Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _tool.RunCommand(commandLine: "Get-Location", cwd: "../outside_dir");
        });
    }

    [Fact]
    public async Task Adversarial_Cancellation_ForcefullyTerminatesContainer()
    {
        using var cts = new CancellationTokenSource();
        var runTask = _tool.RunCommand(commandLine: "Start-Sleep -Seconds 20", ct: cts.Token);

        await Task.Delay(500);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await runTask;
        });
    }

    [Fact]
    public async Task Adversarial_BackgroundProcess_TrackedAndCompleted()
    {
        var launchResult = await _tool.RunCommand(commandLine: "Start-Sleep -Milliseconds 500; echo 'bg-done'", background: true);
        Assert.Contains("Background process launched successfully", launchResult);

        var lines = launchResult.Split('\n');
        var idLine = Array.Find(lines, l => l.StartsWith("CommandId:"));
        var commandId = idLine!.Replace("CommandId:", "").Trim();

        string status = "";
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(150);
            status = await _tool.RunCommand(commandId: commandId);
            if (status.Contains("Status: Completed"))
                break;
        }

        Assert.Contains("Status: Completed (Exit code: 0)", status);
        Assert.Contains("bg-done", status);
    }
}
