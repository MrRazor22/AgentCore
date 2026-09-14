using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;

namespace AgentCore.MultiAgent;

public sealed class SendAgentTool(IAgentNetwork network, string sender) : ITool
{
    private static readonly JsonSchema Schema = new JsonSchemaBuilder()
        .Type<object>()
        .AddProperty("agent", new JsonSchemaBuilder().Type<string>().Description("The name of the agent to send the task or question to.").Build(), required: true)
        .AddProperty("task", new JsonSchemaBuilder().Type<string>().Description("The specific task, instruction, or question for the agent.").Build(), required: true)
        .Build();

    public ToolDefinition Info => new(
        "send_agent",
        FormatDescription(),
        Schema);

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var recipient = (string?)arguments?["agent"] ?? string.Empty;
        var task = (string?)arguments?["task"] ?? string.Empty;

        await foreach (var evt in network.SendStreamingAsync(sender, recipient, [new Text(task)], ct).ConfigureAwait(false))
            yield return evt;
    }

    private string FormatDescription()
    {
        var agents = network.GetAgents(sender);
        if (agents.Count == 0)
            return "Send a task or question to another agent. (No other agents currently available.)";

        var sb = new StringBuilder("Send a task or question to another agent.\nAvailable agents:\n");
        foreach (var a in agents)
            sb.Append($"- {a.Name}: {a.Description}\n");
        return sb.ToString().TrimEnd();
    }
}
