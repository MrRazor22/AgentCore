using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;

namespace AgentCore.MultiAgent.Tools;

public sealed class SendAgentTool(IAgentNetwork network, IAgentRouter router, string sender) : ITool
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
        await Task.CompletedTask;
        var recipient = (string)arguments["agent"]!;
        var task = (string)arguments["task"]!;

        network.Send(sender, recipient, [new Text(task)]);
        yield return new Text($"Message delivered to {recipient}.");
    }

    private string FormatDescription()
    {
        router.Agents.TryGetValue(sender, out var senderEntry);
        var allowed = senderEntry?.Collaborators;

        var available = router.Agents
            .Where(kv => !string.Equals(kv.Key, sender, StringComparison.OrdinalIgnoreCase))
            .Where(kv => allowed == null || allowed.Contains(kv.Key))
            .ToArray();

        if (available.Length == 0)
            return "Send a task or question to another agent. (No other agents currently available.)";

        var sb = new StringBuilder("Send a task or question to another agent.\nAvailable agents:\n");
        foreach (var (name, entry) in available)
        {
            var desc = !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description : "Collaborator";
            sb.Append($"- {name}: {desc}\n");
        }
        return sb.ToString().TrimEnd();
    }
}
