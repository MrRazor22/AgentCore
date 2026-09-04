using System.ComponentModel;
using System.Text.Json.Nodes;
using AgentCore.Tools;

namespace CodeSharp.Tools;

/// <summary>
/// Tool for scheduling future agent wakeups and recurring cron prompts.
/// </summary>
public sealed class ScheduleTool
{
    private int _nextId = 1;
    private readonly Dictionary<int, string> _schedules = new();
    private readonly object _lock = new();

    [Tool("Schedule", "Create or cancel future agent invocation timers and schedules.")]
    public string Schedule(
        [Description("Action to perform: 'create' | 'list' | 'cancel'")] string action,
        [Description("One-shot delay in seconds (for 'create').")] int? afterSeconds = null,
        [Description("Cron expression for recurring trigger (for 'create').")] string? cron = null,
        [Description("Instruction prompt to supply on trigger (for 'create').")] string? prompt = null,
        [Description("Schedule ID to cancel (for 'cancel').")] int? scheduleId = null,
        [Description("Max runs before stopping recurring schedule.")] int? maxRuns = null)
    {
        lock (_lock)
        {
            switch (action.ToLowerInvariant())
            {
                case "create":
                    if (afterSeconds == null && string.IsNullOrWhiteSpace(cron))
                        return "Error: Either 'afterSeconds' or 'cron' must be provided when creating a schedule.";

                    if (string.IsNullOrWhiteSpace(prompt))
                        return "Error: 'prompt' instruction is required when creating a schedule.";

                    int id = _nextId++;
                    var desc = afterSeconds != null
                        ? $"One-shot in {afterSeconds}s"
                        : $"Cron '{cron}' (max runs: {maxRuns?.ToString() ?? "unlimited"})";

                    _schedules[id] = $"{desc} => Prompt: \"{prompt}\"";

                    return $"Schedule #{id} created successfully. ({desc})";

                case "cancel":
                    if (scheduleId == null)
                        return "Error: 'scheduleId' is required for cancel action.";

                    if (_schedules.Remove(scheduleId.Value))
                        return $"Schedule #{scheduleId} cancelled successfully.";

                    return $"Error: Schedule #{scheduleId} not found.";

                case "list":
                    if (_schedules.Count == 0)
                        return "No active schedules.";

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Active Schedules:");
                    foreach (var kvp in _schedules)
                    {
                        sb.AppendLine($"  #{kvp.Key}: {kvp.Value}");
                    }
                    return sb.ToString().TrimEnd();

                default:
                    return $"Error: Unknown schedule action '{action}'. Supported: create, cancel, list.";
            }
        }
    }
}
