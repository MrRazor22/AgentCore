using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AgentCore.Tooling;

namespace TestApp.BuiltInTools;

public class CommandLineTools
{
    private static readonly string[] Whitelist = { "ipconfig", "dir", "whoami", "echo", "hostname" };

    [Tool("run_cmd", "Runs basic whitelisted terminal commands (ipconfig, dir, whoami, echo, hostname). Requires approval.", RequiresApproval = true)]
    public async Task<string> RunCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Error: Command cannot be empty.";
        }

        var cmdName = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (cmdName == null || !Whitelist.Contains(cmdName.ToLowerInvariant()))
        {
            return $"Error: Command '{cmdName}' is not in the whitelist. Whitelisted commands: {string.Join(", ", Whitelist)}";
        }

        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var shell = isWindows ? "cmd.exe" : "/bin/sh";
            var args = isWindows ? $"/c \"{command}\"" : $"-c \"{command}\"";

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return "Error: Failed to start process.";
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var result = stdout + stderr;
            return string.IsNullOrWhiteSpace(result) ? "Command executed with no output." : result;
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }
}
