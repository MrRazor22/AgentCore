using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;

namespace AgentCore.MultiAgent.Tools;

public sealed class CreateAgentTool(IAgentNetwork network, string sender) : ITool
{
    private static readonly JsonSchema Schema = new JsonSchemaBuilder()
        .Type<object>()
        .AddProperty("name", new JsonSchemaBuilder().Type<string>().Description("A unique, concise name for the new agent (e.g. 'coder', 'researcher').").Build(), required: true)
        .AddProperty("role", new JsonSchemaBuilder().Type<string>().Description("The role, responsibilities, and instructions for the new agent.").Build(), required: true)
        .AddProperty("collaborators", new JsonSchemaBuilder().Type<string[]>().Description("Names of other existing agents this new agent is allowed to collaborate with (optional).").Build(), required: false)
        .Build();

    public ToolDefinition Info { get; } = new(
        "create_agent",
        "Dynamically create a new specialized agent to collaborate in the network.",
        Schema);

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var name = (string?)arguments?["name"] ?? string.Empty;
        var role = (string?)arguments?["role"] ?? string.Empty;
        var collaborators = arguments?["collaborators"] is JsonArray cArr
            ? cArr.Select(n => (string?)n).OfType<string>()
            : null;

        var result = network.CreateAgent(sender, name, role, collaborators);
        yield return new Text(result);
    }
}
