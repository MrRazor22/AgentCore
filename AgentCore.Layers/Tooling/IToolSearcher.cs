namespace AgentCore.Tooling;

public interface IToolSearcher
{
    IReadOnlyList<ToolDefinition> Search(string query, IReadOnlyList<ToolDefinition> catalog);
}

public sealed class KeywordToolSearcher : IToolSearcher
{
    public IReadOnlyList<ToolDefinition> Search(string query, IReadOnlyList<ToolDefinition> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.Trim);
        var matches = new List<ToolDefinition>();

        foreach (var def in catalog)
        {
            if (terms.Any(t => def.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                               def.Description.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(def);
            }
        }

        return matches;
    }
}
