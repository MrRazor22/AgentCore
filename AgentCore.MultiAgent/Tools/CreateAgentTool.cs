using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.MultiAgent.Tools;

public sealed class CreateAgentTool(
    IAgentTeam team,
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
        "Dynamically create a new specialized agent to collaborate in the team.",
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
        builder.WithInstructions([new Text(role)]);
        builder.UseTool(t => t.WithTools(new SendAgentTool(team, name)));

        var childCollaborators = collaborators != null
            ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) { sender }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sender };

        team.Add(new TeamMember(name, builder.Build(), childCollaborators, description: role));

        if (team.Members.TryGetValue(sender, out var creatorMember) && creatorMember.Collaborators != null)
        {
            creatorMember.Collaborators.Add(name);
        }

        yield return new Text($"Agent '{name}' created successfully and joined the team.");
    }
}
