namespace AgentCore.Tokens;

public readonly record struct TokenBudget(int Tokens)
{
    public static implicit operator TokenBudget(int tokens) => new(tokens);
}
