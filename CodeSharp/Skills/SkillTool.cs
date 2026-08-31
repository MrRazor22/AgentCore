using System.ComponentModel;
using AgentCore.Tools;

namespace CodeSharp.Skills;

/// <summary>
/// Agent-facing adapter tool to view discovered skill instructions.
/// </summary>
public sealed class SkillTool
{
    private readonly SkillManager _skillManager;

    public SkillTool(SkillManager skillManager)
    {
        _skillManager = skillManager ?? throw new System.ArgumentNullException(nameof(skillManager));
    }

    [Tool("ViewSkill", "Loads and views the complete, detailed instructions for an available skill by name.")]
    public string ViewSkill(
        [Description("The exact name of the skill to view (e.g. 'codeagent').")] string skillName)
    {
        return _skillManager.LoadSkillContent(skillName);
    }
}
