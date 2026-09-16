using System;
using System.Collections.Generic;
using System.Text;
using VisualStudioAgent.Abstractions;

namespace VisualStudioAgent.Services;

public sealed class TodoManager : ITodoManager
{
    private readonly List<string> _todos = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> Todos
    {
        get { lock (_lock) return _todos.ToArray(); }
    }

    public event Action<IReadOnlyList<string>>? TodosChanged;

    public string Update(string[]? todos)
    {
        lock (_lock)
        {
            if (todos != null && todos.Length > 0)
            {
                _todos.Clear();
                _todos.AddRange(todos);
                TodosChanged?.Invoke(_todos.ToArray());
            }

            if (_todos.Count == 0) return "Todo list is currently empty.";

            var sb = new StringBuilder("Current Session Todo List:\n");
            for (int i = 0; i < _todos.Count; i++)
                sb.AppendLine($"  {i + 1}. [ ] {_todos[i]}");
            return sb.ToString().TrimEnd();
        }
    }
}
