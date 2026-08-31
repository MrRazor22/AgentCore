using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeSharp.Security;

public sealed class DockerSandbox
{
    private readonly string _canonicalWorkspace;
    private readonly string _image;
    private readonly string _memoryLimit;
    private readonly string _cpuLimit;
    private readonly int _pidsLimit;

    public DockerSandbox(
        string workspaceRoot,
        string image = "mcr.microsoft.com/powershell:lts-alpine",
        string memoryLimit = "1g",
        string cpuLimit = "2",
        int pidsLimit = 64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _canonicalWorkspace = Path.GetFullPath(workspaceRoot);
        _image = image;
        _memoryLimit = memoryLimit;
        _cpuLimit = cpuLimit;
        _pidsLimit = pidsLimit;
    }

    public async Task<SandboxExecutionResult> ExecuteAsync(
        string commandLine,
        string workingDirectory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);

        // Normalize host path for Docker volume mounting
        var hostMountPath = _canonicalWorkspace.Replace('\\', '/');

        // Resolve container working directory relative to /workspace
        var canonicalCwd = Path.GetFullPath(workingDirectory);
        var relativeCwd = Path.GetRelativePath(_canonicalWorkspace, canonicalCwd).Replace('\\', '/');
        var containerCwd = relativeCwd == "." ? "/workspace" : $"/workspace/{relativeCwd}";

        var containerName = "cs_sandbox_" + Guid.NewGuid().ToString("N")[..8];

        // Base64 encode the PowerShell command to prevent shell interpretation issues
        var bytes = Encoding.Unicode.GetBytes(commandLine);
        var encoded = Convert.ToBase64String(bytes);

        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Use ArgumentList to eliminate CLI injection and quoting corruption
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--rm");
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(containerName);
        psi.ArgumentList.Add("--network");
        psi.ArgumentList.Add("none");
        psi.ArgumentList.Add("--memory");
        psi.ArgumentList.Add(_memoryLimit);
        psi.ArgumentList.Add("--cpus");
        psi.ArgumentList.Add(_cpuLimit);
        psi.ArgumentList.Add("--pids-limit");
        psi.ArgumentList.Add(_pidsLimit.ToString());
        psi.ArgumentList.Add("--security-opt=no-new-privileges");
        psi.ArgumentList.Add("--cap-drop=ALL");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add($"{hostMountPath}:/workspace:rw");
        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add(containerCwd);
        psi.ArgumentList.Add(_image);
        psi.ArgumentList.Add("pwsh");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) lock (stdoutBuilder) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) lock (stderrBuilder) stderrBuilder.AppendLine(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start docker process.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Docker is not available or failed to execute. Ensure Docker Desktop is installed and running.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Forcefully remove container on cancellation/timeout
            try
            {
                var stopPsi = new ProcessStartInfo
                {
                    FileName = "docker",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                stopPsi.ArgumentList.Add("rm");
                stopPsi.ArgumentList.Add("-f");
                stopPsi.ArgumentList.Add(containerName);

                using var stopProc = Process.Start(stopPsi);
                stopProc?.WaitForExit(3000);
            }
            catch
            {
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }

        string stdout;
        string stderr;
        lock (stdoutBuilder) stdout = stdoutBuilder.ToString();
        lock (stderrBuilder) stderr = stderrBuilder.ToString();

        return new SandboxExecutionResult(
            ExitCode: process.ExitCode,
            StandardOutput: stdout,
            StandardError: stderr
        );
    }
}

public sealed record SandboxExecutionResult(int ExitCode, string StandardOutput, string StandardError);
