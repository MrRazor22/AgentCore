namespace AgentCore.Tokens;

public sealed class ApproximateTokenCounter : ITokenCounter
{
    public int Count(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return 0;
        return payload.Length / 4;
    }
}
