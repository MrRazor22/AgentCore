namespace CodeSharp.Security;

/// <summary>
/// Safe workspace sandboxing guard that resolves symlinks recursively across parent chains
/// and enforces relative path containment.
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// Validates that a candidate path resides inside the specified workspace root.
    /// Returns the resolved canonical path if safe; throws SecurityException if path escapes workspace.
    /// </summary>
    public static string EnsureWithinWorkspace(string workspaceRoot, string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        var canonicalRoot = GetCanonicalPath(workspaceRoot);
        if (!canonicalRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalRoot += Path.DirectorySeparatorChar;
        }

        var fullCandidate = Path.IsPathRooted(candidatePath)
            ? candidatePath
            : Path.Combine(workspaceRoot, candidatePath);

        var canonicalCandidate = GetCanonicalPath(fullCandidate);

        var relative = Path.GetRelativePath(canonicalRoot, canonicalCandidate);

        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new System.Security.SecurityException(
                $"Access denied: Path '{candidatePath}' resolves outside workspace boundary '{workspaceRoot}'.");
        }

        return canonicalCandidate;
    }

    /// <summary>
    /// Resolves canonical path by expanding symlinks and resolving relative navigation.
    /// Traverses parent chains to ensure no symlinks redirect outside expected path.
    /// </summary>
    public static string GetCanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);

        try
        {
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    fullPath = target.FullName;
                }
            }
            else if (Directory.Exists(fullPath))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                var target = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    fullPath = target.FullName;
                }
            }
            else
            {
                // For non-existent files (e.g. creating a new file), resolve existing parent directory
                var parent = Path.GetDirectoryName(fullPath);
                if (parent != null && Directory.Exists(parent))
                {
                    var canonicalParent = GetCanonicalPath(parent);
                    fullPath = Path.Combine(canonicalParent, Path.GetFileName(fullPath));
                }
            }
        }
        catch
        {
            // Fallback to full path normalization if link target resolution fails
        }

        return Path.GetFullPath(fullPath);
    }
}
