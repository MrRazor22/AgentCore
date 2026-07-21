using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Tools;

namespace AgentCore.Example;

public class WorkspaceTools
{
    private static readonly HttpClient _httpClient = new();
    private const string TasksFile = "tasks.json";

    public class TaskItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "Pending"; // Pending, In Progress, Completed
    }

    private List<TaskItem> LoadTasks()
    {
        try
        {
            if (File.Exists(TasksFile))
            {
                var json = File.ReadAllText(TasksFile);
                return JsonSerializer.Deserialize<List<TaskItem>>(json) ?? new List<TaskItem>();
            }
        }
        catch
        {
            // Fallback to empty list if corrupted or unreadable
        }
        return new List<TaskItem>();
    }

    private void SaveTasks(List<TaskItem> tasks)
    {
        try
        {
            var json = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TasksFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error saving tasks] {ex.Message}");
        }
    }

    [Tool]
    [Description("Lists all tasks in the agent's workspace to-do/task tracker list.")]
    public string ListTasks()
    {
        var tasks = LoadTasks();
        if (tasks.Count == 0)
        {
            return "No tasks found in the task list.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Current Task List ===");
        foreach (var task in tasks)
        {
            sb.AppendLine($"ID: {task.Id} | Status: [{task.Status}] | {task.Description}");
        }
        return sb.ToString();
    }

    [Tool]
    [Description("Adds a new task to the workspace to-do/task tracker list.")]
    public string AddTask(
        [Description("The detailed description of the task to add.")] string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "Error: Task description cannot be empty.";
        }

        var tasks = LoadTasks();
        var newId = tasks.Count > 0 ? tasks.Max(t => t.Id) + 1 : 1;
        var task = new TaskItem
        {
            Id = newId,
            Description = description.Trim(),
            Status = "Pending"
        };
        tasks.Add(task);
        SaveTasks(tasks);

        return $"Successfully added task. ID: {task.Id} | Status: [{task.Status}] | {task.Description}";
    }

    [Tool]
    [Description("Updates the status of an existing task in the workspace task tracker. Valid statuses are: Pending, In Progress, Completed.")]
    public string UpdateTask(
        [Description("The integer ID of the task to update.")] int id,
        [Description("The new status (Pending, In Progress, or Completed).")] string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Error: Status cannot be empty.";
        }

        var validStatuses = new[] { "Pending", "In Progress", "Completed" };
        var normalizedStatus = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(status.Trim().ToLower());
        if (!validStatuses.Contains(normalizedStatus))
        {
            return $"Error: Invalid status '{status}'. Valid options are: Pending, In Progress, Completed.";
        }

        var tasks = LoadTasks();
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            return $"Error: Task with ID {id} not found.";
        }

        var oldStatus = task.Status;
        task.Status = normalizedStatus;
        SaveTasks(tasks);

        return $"Successfully updated task {id} status from '{oldStatus}' to '{normalizedStatus}'.";
    }

    [Tool]
    [Description("Deletes a task from the workspace task tracker by ID.")]
    public string DeleteTask(
        [Description("The integer ID of the task to delete.")] int id)
    {
        var tasks = LoadTasks();
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            return $"Error: Task with ID {id} not found.";
        }

        tasks.Remove(task);
        SaveTasks(tasks);

        return $"Successfully deleted task {id}: '{task.Description}'.";
    }

    [Tool]
    [Description("Lists files and directories in a given path (non-recursively). Helps with workspace navigation.")]
    public string ListDirectory(
        [Description("The directory path to list. Defaults to '.'.")] string path = ".")
    {
        try
        {
            if (!Directory.Exists(path)) return $"Error: Directory '{path}' does not exist.";
            var entries = Directory.GetFileSystemEntries(path);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Directory contents of '{Path.GetFullPath(path)}':");
            foreach (var entry in entries)
            {
                var isDir = Directory.Exists(entry);
                sb.AppendLine($"{(isDir ? "[DIR] " : "[FILE]")} {Path.GetFileName(entry)}");
            }
            return sb.Length == 0 ? "(Empty directory)" : sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }

    [Tool]
    [Description("Finds files by matching their names against a search query inside a directory (searches recursively).")]
    public string FindFiles(
        [Description("Partial name or pattern of the file to find (e.g., 'Program' or 'cs').")] string query,
        [Description("The directory path to search in. Defaults to '.'.")] string directory = ".")
    {
        try
        {
            if (!Directory.Exists(directory)) return $"Error: Directory '{directory}' does not exist.";
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            var sb = new System.Text.StringBuilder();
            int matchCount = 0;
            foreach (var file in files)
            {
                if (file.Contains("\\.git\\") || file.Contains("\\bin\\") || file.Contains("\\obj\\") || file.Contains("\\.vs\\"))
                    continue;

                var fileName = Path.GetFileName(file);
                if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(Path.GetRelativePath(directory, file));
                    matchCount++;
                    if (matchCount > 50)
                    {
                        sb.AppendLine("... truncated (more than 50 results) ...");
                        break;
                    }
                }
            }
            return sb.Length == 0 ? "No matching files found." : sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error finding files: {ex.Message}";
        }
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
        [Description("The exact command string to run in powershell.")] string command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = System.Text.Encoding.Unicode.GetBytes(command);
            var base64Command = Convert.ToBase64String(bytes);

            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = $"-NoProfile -NonInteractive -EncodedCommand {base64Command}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var token = linkedCts.Token;

            process.Start();

            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(token);
                var errorTask = process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    return $"Command exited with error code {process.ExitCode}.\nOutput: {output}\nError: {error}";
                }
                return string.IsNullOrEmpty(output) ? "(Command succeeded with no output)" : output;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore errors when killing
                }

                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return "Error: Command timed out after 60 seconds.";
                }
                throw;
            }
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
