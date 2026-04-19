using System.ComponentModel;
using System.Text;

namespace AgentCore.Tooling;

/// <summary>
/// A skill is named markdown content that the agent can load on demand.
/// Follows Anthropic/OpenCode standard: markdown files with YAML frontmatter.
/// </summary>
public sealed record Skill(string Name, string Description, string Content, string? Location = null);

/// <summary>
/// Tools for the agent to discover and load skills.
/// </summary>
public sealed class SkillTools(IReadOnlyList<Skill> skills)
{
    [Description("Load a specialized skill by name. Returns the skill content as context for the task.")]
    public string LoadSkill(
        [Description("The name of the skill to load")] string name)
    {
        var skill = skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        
        if (skill == null)
        {
            var available = string.Join(", ", skills.Select(s => s.Name));
            return $"Error: Skill '{name}' not found. Available skills: {available}";
        }
        
        return $"<skill_content>\n{skill.Content}\n</skill_content>";
    }
}

/// <summary>
/// Static methods for loading skills from filesystem.
/// </summary>
public static class SkillLoader
{
    /// <summary>Scan a directory recursively for SKILL.md files and load them.</summary>
    public static List<Skill> FromDirectory(string path)
    {
        var skills = new List<Skill>();
        
        if (!Directory.Exists(path))
            return skills;
        
        foreach (var file in Directory.EnumerateFiles(path, "SKILL.md", SearchOption.AllDirectories))
        {
            try
            {
                var skill = FromMarkdown(file);
                if (skill != null)
                    skills.Add(skill);
            }
            catch (Exception ex)
            {
                // Skip files that fail to parse
                System.Diagnostics.Debug.WriteLine($"Failed to load skill from {file}: {ex.Message}");
            }
        }
        
        return skills;
    }
    
    /// <summary>Parse a single SKILL.md file with YAML frontmatter.</summary>
    public static Skill? FromMarkdown(string path)
    {
        if (!File.Exists(path))
            return null;
        
        var content = File.ReadAllText(path);
        return FromMarkdownContent(content, path);
    }
    
    /// <summary>Parse skill content from markdown string with YAML frontmatter.</summary>
    public static Skill? FromMarkdownContent(string markdown, string? location = null)
    {
        var lines = markdown.Split('\n');
        
        // Check for YAML frontmatter (starts with ---)
        if (lines.Length < 3 || !lines[0].Trim().Equals("---", StringComparison.Ordinal))
        {
            // No frontmatter, use simple format: first line as name, second as description
            if (lines.Length >= 2)
            {
                return new Skill(
                    lines[0].Trim(),
                    lines[1].Trim(),
                    markdown,
                    location);
            }
            return null;
        }
        
        // Parse YAML frontmatter
        var frontmatterEnd = Array.IndexOf(lines, "---", 1);
        if (frontmatterEnd == -1)
            return null;
        
        string? name = null;
        string? description = null;
        
        for (int i = 1; i < frontmatterEnd; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                name = line.Substring(5).Trim();
            else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                description = line.Substring("description:".Length).Trim();
        }
        
        if (string.IsNullOrEmpty(name))
            return null;
        
        // Content is everything after the frontmatter
        var contentBuilder = new StringBuilder();
        for (int i = frontmatterEnd + 1; i < lines.Length; i++)
        {
            contentBuilder.AppendLine(lines[i]);
        }
        
        return new Skill(
            name,
            description ?? "No description",
            contentBuilder.ToString().Trim(),
            location);
    }
}
