using System.ComponentModel;
using AgentCore.Tools;
using CodeSharp.Security;

namespace CodeSharp.Tools;

/// <summary>
/// Static tool methods for file viewing, editing, and filesystem manipulation.
/// </summary>
public static class FileTools
{
    private static string _workspaceRoot = Environment.CurrentDirectory;

    public static void Initialize(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    [Tool("ReadFile", "Read lines from a text file within a specified line range.")]
    public static string ReadFile(
        [Description("Absolute or relative path to the file, relative to the workspace root. Use standard paths such as 'AgentCore/Tools/Tool.cs'. Do not use URI schemes or prefixes such as 'solutionrelative:'.")] string filePath,
        [Description("Starting line number (1-based, default: 1).")] int startLine = 1,
        [Description("Ending line number (inclusive, default: 800 lines max).")] int endLine = 0)
    {
        var targetPath = PathGuard.EnsureWithinWorkspace(_workspaceRoot, filePath);

        if (!File.Exists(targetPath))
            return $"Error: File '{filePath}' does not exist.";

        var allLines = File.ReadAllLines(targetPath);
        if (allLines.Length == 0)
            return $"[File '{filePath}' is empty]";

        startLine = Math.Max(1, startLine);
        if (endLine <= 0 || endLine < startLine)
        {
            endLine = Math.Min(allLines.Length, startLine + 799);
        }
        else
        {
            endLine = Math.Min(allLines.Length, endLine);
        }

        if (startLine > allLines.Length)
            return $"Error: startLine {startLine} exceeds total lines ({allLines.Length}).";

        int count = endLine - startLine + 1;
        var linesSlice = allLines.Skip(startLine - 1).Take(count).ToList();
        var formatted = OutputHelpers.FormatWithLineNumbers(linesSlice, startLine);

        if (endLine < allLines.Length)
        {
            formatted += $"\n\n[Content limited to lines {startLine}-{endLine} of {allLines.Length}. Continue with startLine={endLine + 1}.]";
        }

        return formatted;
    }

    [Tool("EditFile", "Edit an existing file by replacing exact target text, or create a new file.")]
    public static string EditFile(
        [Description("Path to the file to edit or create, relative to the workspace root. Use standard paths such as 'AgentCore/Tools/Tool.cs'. Do not use URI schemes or prefixes such as 'solutionrelative:'.")] string filePath,
        [Description("Target exact text substring to replace. Leave empty to create a new file.")] string targetContent,
        [Description("Replacement content text.")] string replacementContent,
        [Description("Advisory risk assessment flag.")] bool safeToAutoRun = false)
    {
        var targetPath = PathGuard.EnsureWithinWorkspace(_workspaceRoot, filePath);

        if (!File.Exists(targetPath))
        {
            if (!string.IsNullOrEmpty(targetContent))
                return $"Error: Cannot replace text in non-existent file '{filePath}'. Leave targetContent empty to create a new file.";

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(targetPath, replacementContent ?? string.Empty);
            return $"Successfully created file '{filePath}'.";
        }

        var content = File.ReadAllText(targetPath);
        if (string.IsNullOrEmpty(targetContent))
            return "Error: File exists. To replace content, provide targetContent matching exact existing code block.";

        int firstIdx = content.IndexOf(targetContent, StringComparison.Ordinal);
        if (firstIdx < 0)
            return "Error: targetContent not found in file. Ensure exact character and whitespace match.";

        int secondIdx = content.IndexOf(targetContent, firstIdx + targetContent.Length, StringComparison.Ordinal);
        if (secondIdx >= 0)
            return "Error: targetContent matched multiple locations in file. Provide more context to make the match unique.";

        var updated = content.Remove(firstIdx, targetContent.Length).Insert(firstIdx, replacementContent ?? string.Empty);

        var tempPath = targetPath + ".tmp_" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, updated);
        File.Move(tempPath, targetPath, overwrite: true);

        return $"Successfully updated '{filePath}'.";
    }

    [Tool("Filesystem", "Perform file filesystem operations: move, copy, delete.")]
    public static string Filesystem(
        [Description("Action type: 'move' | 'copy' | 'delete'")] string action,
        [Description("Target file or directory path, relative to the workspace root. Use standard paths such as 'AgentCore/Tools/Tool.cs'. Do not use URI schemes or prefixes such as 'solutionrelative:'.")] string path,
        [Description("Destination path (required for move/copy), relative to the workspace root. Use standard paths such as 'AgentCore/Tools/Tool.cs'. Do not use URI schemes or prefixes such as 'solutionrelative:'.")] string? destination = null,
        [Description("Advisory risk assessment flag.")] bool safeToAutoRun = false)
    {
        var targetPath = PathGuard.EnsureWithinWorkspace(_workspaceRoot, path);

        switch (action.ToLowerInvariant())
        {
            case "delete":
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    return $"Deleted file '{path}'.";
                }
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, recursive: true);
                    return $"Deleted directory '{path}'.";
                }
                return $"Error: Path '{path}' not found.";

            case "move":
                if (string.IsNullOrWhiteSpace(destination))
                    return "Error: destination is required for 'move' action.";
                var destPathMove = PathGuard.EnsureWithinWorkspace(_workspaceRoot, destination);
                if (File.Exists(targetPath))
                {
                    var dir = Path.GetDirectoryName(destPathMove);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Move(targetPath, destPathMove, overwrite: true);
                    return $"Moved file '{path}' to '{destination}'.";
                }
                if (Directory.Exists(targetPath))
                {
                    Directory.Move(targetPath, destPathMove);
                    return $"Moved directory '{path}' to '{destination}'.";
                }
                return $"Error: Source path '{path}' not found.";

            case "copy":
                if (string.IsNullOrWhiteSpace(destination))
                    return "Error: destination is required for 'copy' action.";
                var destPathCopy = PathGuard.EnsureWithinWorkspace(_workspaceRoot, destination);
                if (File.Exists(targetPath))
                {
                    var dir = Path.GetDirectoryName(destPathCopy);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(targetPath, destPathCopy, overwrite: true);
                    return $"Copied file '{path}' to '{destination}'.";
                }
                return $"Error: Copy action is currently supported for files. Path '{path}' not found.";

            default:
                return $"Error: Unknown filesystem action '{action}'. Supported: move, copy, delete.";
        }
    }
}
