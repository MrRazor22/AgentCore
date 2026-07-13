using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace AgentCore.Tools;

public interface IToolRegistry
{
    IReadOnlyList<Tool> Tools { get; }
    void Add(Tool tool);
    bool Remove(string name);
    Tool? TryGet(string name);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, Tool> _registry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ToolRegistry> _logger;
    public IReadOnlyList<Tool> Tools => _registry.Values.ToArray();

    public ToolRegistry(ILogger<ToolRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<ToolRegistry>.Instance;
    }

    public void Add(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));

        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("Tool name is required.", nameof(tool));

        if (_registry.ContainsKey(tool.Name))
        {
            _logger.LogWarning("Tool registration failed: ToolName={ToolName} Duplicate", tool.Name);
            throw new InvalidOperationException($"Duplicate tool name '{tool.Name}'.");
        }

        _registry[tool.Name] = tool;
        _logger.LogInformation("Tool registered: ToolName={ToolName}", tool.Name);
    }

    public bool Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name is required.", nameof(name));

        var removed = _registry.TryRemove(name, out _);
        _logger.LogDebug("Tool unregistered: ToolName={ToolName} Success={Success}", name, removed);
        return removed;
    }

    public Tool? TryGet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name is required.", nameof(name));

        var found = _registry.TryGetValue(name, out var entry);
        _logger.LogDebug("Tool lookup: ToolName={ToolName} Found={Found}", name, found);
        return entry;
    }
}
