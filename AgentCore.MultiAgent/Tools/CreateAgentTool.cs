using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using AgentCore.Tool.Tools;

namespace AgentCore.MultiAgent.Tools;

public sealed class CreateAgentTool(
    IAgentTeam team,
    string sender,
    Agent template) : ITool
{
    private readonly Agent _template = template ?? throw new ArgumentNullException(nameof(template));

    private static readonly JsonSchema Schema = new JsonSchemaBuilder()
        .Type<object>()
        .AddProperty("name", new JsonSchemaBuilder().Type<string>().Description("A unique, concise name for the new agent (e.g. 'coder', 'researcher').").Build(), required: true)
        .AddProperty("role", new JsonSchemaBuilder().Type<string>().Description("The role, responsibilities, and instructions for the new agent.").Build(), required: true)
        .AddProperty("collaborators", new JsonSchemaBuilder().Type<string[]>().Description("Names of other existing agents this new agent is allowed to collaborate with (optional).").Build(), required: false)
        .Build();

    public ToolDefinition Definition { get; } = new(
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

        var childCollaborators = collaborators != null
            ? new HashSet<string>(collaborators, StringComparer.OrdinalIgnoreCase) { sender }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sender };

        var tools = await _template.Toolbox.GetToolsAsync(ct).ConfigureAwait(false);
        var tooling = new Toolbox(tools).AddTool(new SendAgentTool(team, name));
        var newAgent = _template
            .With(instructions: [new Text(role)], toolbox: tooling);

        team.Add(new TeamMember(name, newAgent, childCollaborators, description: role));

        if (team.Members.TryGetValue(sender, out var creatorMember) && creatorMember.Collaborators != null)
        {
            creatorMember.Collaborators.Add(name);
        }

        yield return new Text($"Agent '{name}' created successfully and joined the team.");
    }
}
