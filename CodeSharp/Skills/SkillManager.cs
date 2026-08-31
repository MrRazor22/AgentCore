using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeSharp.Skills;

/// <summary>
/// Discovers, parses metadata, and loads content for skills.
/// </summary>
public sealed class SkillManager
{
    private readonly Dictionary<string, Skill> _skills = new(StringComparer.OrdinalIgnoreCase);

    public SkillManager(string? workspaceRoot = null, IEnumerable<string>? additionalSearchPaths = null)
    {
        DiscoverSkills(workspaceRoot, additionalSearchPaths);
    }

    public IReadOnlyCollection<Skill> AvailableSkills => _skills.Values;

    public Skill? GetSkill(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _skills.TryGetValue(name.Trim(), out var skill) ? skill : null;
    }

    public string LoadSkillContent(string name)
    {
        var skill = GetSkill(name);
        if (skill == null)
        {
            var available = _skills.Count > 0 ? string.Join(", ", _skills.Keys) : "(none)";
            return $"Error: Skill '{name}' not found. Available skills: {available}";
        }

        try
        {
            return File.ReadAllText(skill.FilePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return $"Error reading skill '{name}': {ex.Message}";
        }
    }

    public string FormatIndex()
    {
        if (_skills.Count == 0) return "(No skills available)";

        var sb = new StringBuilder();
        foreach (var skill in _skills.Values)
        {
            sb.AppendLine($"- {skill.Name}: {skill.Description}");
        }
        return sb.ToString().TrimEnd();
    }

    private void DiscoverSkills(string? workspaceRoot, IEnumerable<string>? additionalSearchPaths)
    {
        var searchDirs = new List<string>();

        // 1. Built-in skills directory (bundled next to app executable)
        var builtinDir = Path.Combine(AppContext.BaseDirectory, "skills");
        if (Directory.Exists(builtinDir)) searchDirs.Add(builtinDir);

        // 2. Global user directory (~/.codesharp/skills)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var globalDir = Path.Combine(userProfile, ".codesharp", "skills");
            if (Directory.Exists(globalDir)) searchDirs.Add(globalDir);
        }

        // 3. Additional fallback search paths
        if (additionalSearchPaths != null)
        {
            foreach (var path in additionalSearchPaths)
            {
                if (Directory.Exists(path)) searchDirs.Add(path);
            }
        }

        // 4. Workspace skills directory (highest priority: .codesharp/skills or .skills)
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var wsCodesharpSkills = Path.Combine(workspaceRoot, ".codesharp", "skills");
            if (Directory.Exists(wsCodesharpSkills)) searchDirs.Add(wsCodesharpSkills);

            var wsSkills = Path.Combine(workspaceRoot, ".skills");
            if (Directory.Exists(wsSkills)) searchDirs.Add(wsSkills);
        }

        foreach (var dir in searchDirs)
        {
            ScanDirectory(dir);
        }
    }

    private void ScanDirectory(string dir)
    {
        try
        {
            // Direct skill files (e.g. dir/*.md)
            foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
            {
                var skill = ParseSkillFile(file);
                if (skill != null) _skills[skill.Name] = skill;
            }

            // Skill folders with SKILL.md (e.g. dir/codeagent/SKILL.md)
            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                var skillMd = Path.Combine(subDir, "SKILL.md");
                if (File.Exists(skillMd))
                {
                    var skill = ParseSkillFile(skillMd, Path.GetFileName(subDir));
                    if (skill != null) _skills[skill.Name] = skill;
                }
            }
        }
        catch
        {
            // Ignore directory scanning errors to ensure resilient startup
        }
    }

    private static Skill? ParseSkillFile(string filePath, string? fallbackName = null)
    {
        try
        {
            var name = fallbackName ?? Path.GetFileNameWithoutExtension(filePath);
            var description = string.Empty;

            var lines = File.ReadAllLines(filePath);
            var inFrontmatter = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (i == 0 && line == "---")
                {
                    inFrontmatter = true;
                    continue;
                }

                if (inFrontmatter)
                {
                    if (line == "---")
                    {
                        inFrontmatter = false;
                        continue;
                    }

                    if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = line["name:".Length..].Trim().Trim('"', '\'');
                        if (!string.IsNullOrEmpty(val)) name = val;
                    }
                    else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = line["description:".Length..].Trim().Trim('"', '\'');
                        if (!string.IsNullOrEmpty(val)) description = val;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(description))
                    {
                        // Use first meaningful heading or sentence if no description in frontmatter
                        if (line.StartsWith('#'))
                        {
                            var heading = line.TrimStart('#').Trim();
                            if (!string.IsNullOrEmpty(heading)) description = heading;
                        }
                        else if (!string.IsNullOrEmpty(line))
                        {
                            description = line;
                        }
                    }

                    if (!string.IsNullOrEmpty(description)) break;
                }
            }

            if (string.IsNullOrEmpty(description))
            {
                description = $"Skill for {name}";
            }

            return new Skill(name, description, Path.GetFullPath(filePath));
        }
        catch
        {
            return null;
        }
    }
}
