using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;

namespace AgentCore.MultiAgent.Tools;

public sealed class CreateAgentTool(
    IAgentRouter router,
    IAgentNetwork network,
    string sender,
    Action<AgentBuilder> configureDefaults) : ITool
{
    private readonly Action<AgentBuilder> _configureDefaults = configureDefaults ?? throw new ArgumentNullException(nameof(configureDefaults));

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
        await Task.CompletedTask;
        var name = (string)arguments["name"]!;
        var role = (string)arguments["role"]!;
        var collaborators = arguments["collaborators"] is JsonArray cArr
            ? cArr.Select(n => (string?)n).OfType<string>()
            : null;

        var builder = new AgentBuilder();
        _configureDefaults(builder);
        builder.WithInstructions(role);
        builder.UseToolbox(t => t.WithTools(new SendAgentTool(network, router, name)));

        var childCollaborators = collaborators != null
            ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) { sender }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sender };

        router.Register(name, builder.Build(), childCollaborators, description: role);

        if (router.Agents.TryGetValue(sender, out var creatorEntry) && creatorEntry.Collaborators != null)
        {
            creatorEntry.Collaborators.Add(name);
        }

        yield return new Text($"Agent '{name}' created successfully and joined the network.");
    }
}
