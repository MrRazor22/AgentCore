using System;
using System.IO;
using CodeSharp.Skills;
using Xunit;

namespace CodeSharp.Tests;

public class SkillManagerTests : IDisposable
{
    private readonly string _tempDir;

    public SkillManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SkillManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void SkillManager_DiscoversFolderSkillWithFrontmatter()
    {
        var skillFolder = Path.Combine(_tempDir, "custom-skill");
        Directory.CreateDirectory(skillFolder);
        var skillMd = Path.Combine(skillFolder, "SKILL.md");
        File.WriteAllText(skillMd, @"---
name: custom-skill
description: A custom skill for testing.
---

# Custom Skill Content
Follow these instructions carefully.");

        var manager = new SkillManager(additionalSearchPaths: new[] { _tempDir });
        var skill = manager.GetSkill("custom-skill");

        Assert.NotNull(skill);
        Assert.Equal("custom-skill", skill!.Name);
        Assert.Equal("A custom skill for testing.", skill.Description);

        var content = manager.LoadSkillContent("custom-skill");
        Assert.Contains("# Custom Skill Content", content);
        Assert.Contains("Follow these instructions carefully.", content);
    }

    [Fact]
    public void SkillManager_DiscoversDirectMarkdownFile()
    {
        var file = Path.Combine(_tempDir, "git-workflow.md");
        File.WriteAllText(file, @"# Git Workflows
Best practices for branching and committing.");

        var manager = new SkillManager(additionalSearchPaths: new[] { _tempDir });
        var skill = manager.GetSkill("git-workflow");

        Assert.NotNull(skill);
        Assert.Equal("git-workflow", skill!.Name);
        Assert.Equal("Git Workflows", skill.Description);
    }

    [Fact]
    public void SkillManager_FormatIndex_ReturnsCompactSummary()
    {
        var skillFolder = Path.Combine(_tempDir, "docker-ops");
        Directory.CreateDirectory(skillFolder);
        File.WriteAllText(Path.Combine(skillFolder, "SKILL.md"), @"---
name: docker-ops
description: Docker container management.
---
Instructions");

        var manager = new SkillManager(additionalSearchPaths: new[] { _tempDir });
        var index = manager.FormatIndex();

        Assert.Contains("- docker-ops: Docker container management.", index);
    }

    [Fact]
    public void SkillManager_LoadSkillContent_UnknownSkillReturnsHelpfulError()
    {
        var manager = new SkillManager(additionalSearchPaths: new[] { _tempDir });
        var result = manager.LoadSkillContent("nonexistent-skill");

        Assert.Contains("Error: Skill 'nonexistent-skill' not found", result);
    }

    [Fact]
    public void SkillTool_ViewSkill_ReturnsContentFromManager()
    {
        var skillFolder = Path.Combine(_tempDir, "test-skill");
        Directory.CreateDirectory(skillFolder);
        File.WriteAllText(Path.Combine(skillFolder, "SKILL.md"), "Detailed test skill body.");

        var manager = new SkillManager(additionalSearchPaths: new[] { _tempDir });
        var tool = new SkillTool(manager);

        var content = tool.ViewSkill("test-skill");
        Assert.Equal("Detailed test skill body.", content);
    }

    [Fact]
    public void SkillManager_Precedence_WorkspaceOverridesLowerPrioritySources()
    {
        var lowerPriorityDir = Path.Combine(_tempDir, "lower_priority");
        Directory.CreateDirectory(lowerPriorityDir);
        File.WriteAllText(Path.Combine(lowerPriorityDir, "codeagent.md"), @"---
name: codeagent
description: Base codeagent instructions.
---
Base content");

        var workspaceDir = Path.Combine(_tempDir, "workspace");
        var wsSkillsDir = Path.Combine(workspaceDir, ".codesharp", "skills");
        Directory.CreateDirectory(wsSkillsDir);
        File.WriteAllText(Path.Combine(wsSkillsDir, "codeagent.md"), @"---
name: codeagent
description: Workspace overridden codeagent.
---
Overridden workspace content");

        // Pass lower-priority directory first, workspaceRoot second (which evaluates higher priority in scan order)
        var manager = new SkillManager(workspaceRoot: workspaceDir, additionalSearchPaths: new[] { lowerPriorityDir });
        var skill = manager.GetSkill("codeagent");

        Assert.NotNull(skill);
        Assert.Equal("codeagent", skill!.Name);
        Assert.Equal("Workspace overridden codeagent.", skill.Description);
        var content = manager.LoadSkillContent("codeagent");
        Assert.Contains("Overridden workspace content", content);
    }
}
