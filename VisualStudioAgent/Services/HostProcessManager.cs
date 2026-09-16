using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisualStudioAgent.Abstractions;

namespace VisualStudioAgent.Services;

public sealed class HostProcessManager : IHostProcessManager
{
    private Process? _process;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsRunning => _process != null && !_process.HasExited;

    public async Task StartAsync(Func<string, Task> onMessageReceived)
    {
        if (IsRunning) return;

        string? exe = FindHostExecutable();
        if (exe == null || !File.Exists(exe)) return;

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8
        });

        if (_process == null) return;

        _writer = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine($"[Host] {e.Data}"); };
        _process.BeginErrorReadLine();

        _ = Task.Run(async () =>
        {
            while (_process != null && !_process.HasExited)
            {
                string? line = await _process.StandardOutput.ReadLineAsync();
                if (line == null) break;
                try { await onMessageReceived(line); } catch { }
            }
        });
    }

    public async Task SendAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _writer == null) return;
        await _writeLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(message);
            await _writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Stop()
    {
        try { _process?.Kill(); } catch { }
        _process = null;
        _writer = null;
    }

    public void Dispose() => Stop();

    private static string? FindHostExecutable()
    {
        string? env = Environment.GetEnvironmentVariable("AGENTCORE_HOST_PATH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        string dev = @"D:\CodeBase\AgentCore-Main\AgentCore.Host\bin\Debug\net10.0\AgentCore.Host.exe";
        if (File.Exists(dev)) return dev;

        string baseDir = Path.GetDirectoryName(typeof(HostProcessManager).Assembly.Location) ?? "";
        string devRel = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\AgentCore.Host\bin\Debug\net10.0\AgentCore.Host.exe"));
        if (File.Exists(devRel)) return devRel;

        string local = Path.Combine(baseDir, "AgentCore.Host.exe");
        return File.Exists(local) ? local : null;
    }
}
