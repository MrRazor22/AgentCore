using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VisualStudioAgent.Abstractions;

namespace VisualStudioAgent.Services;

public sealed class VsTerminalService : IVsTerminalService
{
    private static readonly Guid DevinPaneGuid = new("A7312150-1B24-4B2E-8CE1-B33D2D3E3376");

    public async Task<(string Result, bool IsError)> RunCommandAsync(string commandLine, string workingDirectory, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return ("Error: commandLine must be provided.", true);

        await WriteToOutputPaneAsync($"\n> {commandLine}\n");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{commandLine.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var proc = new Process { StartInfo = psi };
            var output = new StringBuilder();

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) { output.AppendLine(e.Data); _ = WriteToOutputPaneAsync(e.Data + "\n"); }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) { output.AppendLine(e.Data); _ = WriteToOutputPaneAsync("[ERR] " + e.Data + "\n"); }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await Task.Run(() => proc.WaitForExit());

            string result = output.ToString().Trim();
            if (result.Length > maxCharacters)
            {
                int half = (maxCharacters - 100) / 2;
                result = $"{result.Substring(0, half)}\n\n[... truncated ...]\n\n{result.Substring(result.Length - half)}";
            }

            return ($"Exit Code: {proc.ExitCode}\n" + (string.IsNullOrWhiteSpace(result) ? "[No output]" : result), proc.ExitCode != 0);
        }
        catch (Exception ex)
        {
            await WriteToOutputPaneAsync("[EXCEPTION] " + ex.Message + "\n");
            return ($"Failed to execute command: {ex.Message}", true);
        }
    }

    private static async Task WriteToOutputPaneAsync(string text)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var outWin = (IVsOutputWindow?)Package.GetGlobalService(typeof(SVsOutputWindow));
            if (outWin == null) return;

            Guid guid = DevinPaneGuid;
            outWin.GetPane(ref guid, out var pane);
            if (pane == null)
            {
                outWin.CreatePane(ref guid, "Devin Agent", 1, 1);
                outWin.GetPane(ref guid, out pane);
            }
            pane?.OutputStringThreadSafe(text);
        }
        catch { }
    }
}
