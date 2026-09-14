namespace CodeSharp.Skills;

/// <summary>
/// Represents a discovered skill with its metadata and file path.
/// </summary>
public sealed record Skill(string Name, string Description, string FilePath);
