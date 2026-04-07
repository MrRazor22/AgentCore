namespace AgentCore.CodingAgent;

public sealed record SandboxPolicy
{
    public IReadOnlyList<string> AllowedNamespaces { get; init; } = ["System", "System.Linq", "System.Collections.Generic", "System.Text", "System.Math"];
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxOutputLength { get; init; } = 4000;

    public static SandboxPolicy Restrictive => new();
    public static SandboxPolicy Permissive => new()
    {
        AllowedNamespaces = ["*"],
        ExecutionTimeout = TimeSpan.FromMinutes(2)
    };
}
