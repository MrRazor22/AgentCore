using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentCore.Conversation;
using AgentCore.Tooling;

namespace AgentCore.CodingAgent;

public sealed class ProcessExecutor : ICSharpExecutor
{
    private readonly SandboxPolicy _policy;
    private readonly string _scriptPath;
    private Process? _currentProcess;

    public ProcessExecutor(SandboxPolicy? policy = null)
    {
        _policy = policy ?? SandboxPolicy.Restrictive;
        _scriptPath = FindDotnetScriptPath();
    }

    private static string FindDotnetScriptPath()
    {
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "";
        var possiblePaths = new[]
        {
            "dotnet-script",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "dotnet-script.dll"),
            @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.0\System.Console.dll",
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "dotnet-script";
    }

    public void SendTools(IReadOnlyList<Tool> tools, IToolExecutor executor)
    {
    }

    public void SendVariables(Dictionary<string, object?> variables)
    {
    }

    public CodeOutput Execute(string codeAction)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"agentcore_{Guid.NewGuid():N}.csx");
        var outputBuilder = new StringBuilder();
        var logsBuilder = new StringBuilder();

        try
        {
            var scriptContent = BuildScript(codeAction);
            File.WriteAllText(tempFile, scriptContent);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet-script",
                Arguments = $"\"{tempFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new CodeOutput(null, "Failed to start dotnet-script process", false);
            }

            _currentProcess = process;

            var timeoutCts = new CancellationTokenSource(_policy.ExecutionTimeout);
            var readTask = ReadOutputAsync(process, outputBuilder, logsBuilder, timeoutCts.Token);

            bool exited = process.WaitForExit((int)_policy.ExecutionTimeout.TotalMilliseconds);
            if (!exited)
            {
                logsBuilder.AppendLine("Process execution timed out");
                return new CodeOutput(null, logsBuilder.ToString(), false);
            }

            var output = outputBuilder.ToString();
            var logs = logsBuilder.ToString();

            if (IsFinalAnswer(output))
            {
                var extractedValue = ExtractFinalAnswerValue(output);
                return new CodeOutput(extractedValue, logs, true);
            }

            var truncatedOutput = output.Length > _policy.MaxOutputLength
                ? output[.._policy.MaxOutputLength] + "\n... (output truncated)"
                : output;

            return new CodeOutput(truncatedOutput, logs, false);
        }
        catch (Exception ex)
        {
            return new CodeOutput(null, $"Execution error: {ex.Message}", false);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
            _currentProcess?.Kill();
        }
    }

    private string BuildScript(string codeAction)
    {
        var usings = new List<string>
        {
            "using System;",
            "using System.Linq;",
            "using System.Collections.Generic;",
            "using System.Text;",
        };

        if (_policy.AllowedNamespaces.Count > 0 && !_policy.AllowedNamespaces.Contains("*"))
        {
            foreach (var ns in _policy.AllowedNamespaces)
            {
                if (ns != "*")
                    usings.Add($"using {ns};");
            }
        }

        var usingsBlock = string.Join("\n", usings);

        return $@"
{usingsBlock}

public static class Script
{{
    public static void Print(object? obj) => Console.WriteLine(obj?.ToString() ?? ""null"");

    public static void FinalAnswer(object? value)
    {{
        Console.WriteLine(""___FINAL_ANSWER___:"" + JsonSerializer.Serialize(value));
        throw new Exception(""FinalAnswer"");
    }}

    public static void Main()
    {{
        try
        {{
{IndentCode(codeAction, "            ")}
        }}
        catch (Exception ex) when (ex.Message == ""FinalAnswer"")
        {{
        }}
    }}
}}
";
    }

    private static string IndentCode(string code, string indent)
    {
        var lines = code.Split('\n');
        var indented = new StringBuilder();
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                indented.AppendLine(indent + line);
        }
        return indented.ToString();
    }

    private static async Task ReadOutputAsync(
        Process process,
        StringBuilder output,
        StringBuilder logs,
        CancellationToken ct)
    {
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
        }

        output.Append(outputTask.Result);
        logs.Append(errorTask.Result);
    }

    private static bool IsFinalAnswer(string output)
    {
        return output.Contains("___FINAL_ANSWER___:");
    }

    private static object? ExtractFinalAnswerValue(string output)
    {
        var match = Regex.Match(output, @"___FINAL_ANSWER___:(.+)");
        if (match.Success)
        {
            try
            {
                return JsonSerializer.Deserialize<object?>(match.Groups[1].Value);
            }
            catch
            {
                return match.Groups[1].Value;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _currentProcess?.Kill();
        _currentProcess?.Dispose();
    }
}
