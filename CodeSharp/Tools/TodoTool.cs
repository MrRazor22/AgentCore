using System.ComponentModel;
using AgentCore.Tools;

namespace CodeSharp.Tools;

/// <summary>
/// Tool for managing session tasks and execution plans.
/// </summary>
public sealed class TodoTool
{
    private readonly List<string> _todos = new();
    private readonly object _lock = new();

    [Tool("TodoList", "Set or update the agent session todo list checklist.")]
    public string TodoList(
        [Description("List of task items/goals to maintain for the session.")] string[]? todos = null)
    {
        lock (_lock)
        {
            if (todos != null && todos.Length > 0)
            {
                _todos.Clear();
                _todos.AddRange(todos);
            }

            if (_todos.Count == 0)
                return "Todo list is currently empty.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Current Session Todo List:");
            for (int i = 0; i < _todos.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. [ ] {_todos[i]}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
