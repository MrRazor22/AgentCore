using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public sealed class AgentTool(IAgent agent, string name, string description) : ITool
{
    private static readonly JsonSchema PromptSchema = new JsonSchemaBuilder()
        .Type<object>()
        .AddProperty("prompt", new JsonSchemaBuilder().Type<string>().Build(), required: true)
        .Build();

    public ToolDefinition Definition { get; } = new(name, description, PromptSchema);

    public async Task<IReadOnlyList<IContent>> InvokeAsync(JsonObject arguments, CancellationToken ct = default)
    {
        var text = await agent.InvokeAsync(new Text((string?)arguments?["prompt"] ?? string.Empty), ct).ConfigureAwait(false);
        return [new Text(text ?? string.Empty)];
    }
}

public static class AgentToolExtensions
{
    public static AgentTool AsTool(this IAgent agent, string name, string description)
        => new(agent, name, description);
}
