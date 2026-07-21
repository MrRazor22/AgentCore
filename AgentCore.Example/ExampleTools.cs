using System.ComponentModel;
using AgentCore.Tools;

namespace AgentCore.Example;

public class ExampleTools
{
    private static readonly HttpClient _httpClient = new();

    [Tool]
    [Description("Evaluates mathematical expressions using a web api. E.g., '2 + 2 * 3'.")]
    public async Task<string> EvaluateMath(
        [Description("The mathematical expression to evaluate.")] string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return "Error: Empty expression.";

        try
        {
            var encoded = Uri.EscapeDataString(expression.Trim());
            var url = $"https://api.mathjs.org/v4/?expr={encoded}";
            using var response = await _httpClient.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode ? result.Trim() : $"Error: {result}";
        }
        catch (Exception ex)
        {
            return $"Error evaluating math: {ex.Message}";
        }
    }

    [Tool]
    [Description("Get weather forecast info for a given city location.")]
    public string GetWeather(
        [Description("The location/city name, e.g. London, Tokyo.")] string location)
    {
        var temp = new Random().Next(15, 32);
        var conditions = new[] { "Sunny", "Cloudy", "Rainy", "Windy" }[new Random().Next(0, 4)];
        return $"The weather in {location} is currently {temp}°C, conditions: {conditions}.";
    }

    [Tool]
    [Description("Reads content from a local text file.")]
    public string ReadTextFile(
        [Description("Absolute or relative path to the text file.")] string path)
    {
        try
        {
            if (!File.Exists(path)) return $"Error: File '{path}' does not exist.";
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    [Tool]
    [Description("Writes text content to a local text file.")]
    public string WriteTextFile(
        [Description("Path to write to.")] string path, 
        [Description("The content to write.")] string content)
    {
        try
        {
            File.WriteAllText(path, content);
            return $"Successfully wrote to file '{path}'.";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    [Tool]
    [Description("Executes a shell command in the local powershell environment (dangerous command, requires user approval).")]
    public async Task<string> ExecuteCommand(
        [Description("The exact command string to run in powershell.")] string command)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return $"Command exited with error code {process.ExitCode}.\nOutput: {output}\nError: {error}";
            }
            return string.IsNullOrEmpty(output) ? "(Command succeeded with no output)" : output;
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    [Tool]
    [Description("Recursively searches files in a directory for lines matching a pattern. Prefers ripgrep (rg) if installed, otherwise falls back to a recursive search.")]
    public async Task<string> SearchFiles(
        [Description("The text pattern or regex to search for.")] string pattern,
        [Description("The directory path to search, e.g. '.'.")] string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return $"Error: Directory '{directory}' does not exist.";
            }

            // Try ripgrep (rg) first
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "rg";
                process.StartInfo.Arguments = $"--line-number --heading --color never \"{pattern}\" \"{directory}\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output;
                }
            }
            catch
            {
                // Fallback to C# recursion
            }

            // Fallback: C# recursive regex search
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var sb = new System.Text.StringBuilder();
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);

            int matchCount = 0;
            foreach (var file in files)
            {
                // Skip binaries, git files, bin/obj folders
                if (file.Contains("\\.git\\") || file.Contains("\\bin\\") || file.Contains("\\obj\\") || file.Contains("\\.vs\\"))
                    continue;

                try
                {
                    var lines = await File.ReadAllLinesAsync(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            sb.AppendLine($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                            matchCount++;
                            if (matchCount > 100)
                            {
                                sb.AppendLine("... truncated (more than 100 results) ...");
                                return sb.ToString();
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore unreadable files
                }
            }

            return sb.Length == 0 ? "No matches found." : sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching files: {ex.Message}";
        }
    }
}
