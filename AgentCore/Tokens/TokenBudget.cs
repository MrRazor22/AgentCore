namespace AgentCore.Tokens;

public readonly record struct TokenBudget(int Tokens)
{
    public static implicit operator TokenBudget(int tokens) => new(tokens);
}

public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0, int ReasoningTokens = 0)
{
    public int Total => InputTokens + OutputTokens + ReasoningTokens;
    public static TokenUsage Empty => new(0, 0, 0);
    public bool IsEmpty => InputTokens == 0 && OutputTokens == 0 && ReasoningTokens == 0;
}
