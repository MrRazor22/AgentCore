using AgentCore.LLM.Chat;

namespace AgentCore.MultiAgent;

public static class MultiAgentExtensions
{
    public static async Task<string?> SendAsync(
        this IAgentNetwork network,
        string sender,
        string recipient,
        string task,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);

        var sb = new System.Text.StringBuilder();
        bool hasDeltas = false;

        await foreach (var evt in network.SendStreamingAsync(sender, recipient, [new Text(task)], ct).ConfigureAwait(false))
        {
            if (evt is TextDelta td) { sb.Append(td.Text); hasDeltas = true; }
            else if (evt is Text t && !hasDeltas) sb.Append(t.Value);
        }

        return sb.ToString();
    }

    public static AgentBuilder WithNetwork(this AgentBuilder builder, AgentNetwork network, string agentName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(agentName);
        return builder.UseToolbox(t => t.WithTools(network.GetToolsFor(agentName).ToArray()));
    }
}
