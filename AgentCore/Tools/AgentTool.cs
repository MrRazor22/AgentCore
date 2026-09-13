using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public sealed class AgentTool(Agent agent, string name, string description) : ITool
{
    private static readonly JsonSchema PromptSchema = new JsonSchemaBuilder()
        .Type<object>()
        .AddProperty("prompt", new JsonSchemaBuilder().Type<string>().Build(), required: true)
        .Build();

    public ToolDefinition Definition { get; } = new(name, description, PromptSchema);

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = (string?)arguments?["prompt"] ?? string.Empty;

        yield return new TextStart(0);

        await foreach (var evt in agent.InvokeStreamingAsync(new Text(prompt), ct).ConfigureAwait(false))
        {
            if (evt is TextDelta td)
            {
                yield return new TextDelta(0, td.Text);
            }
        }

        yield return new TextEnd(0);
    }
}

public static class AgentToolExtensions
{
    public static AgentTool AsTool(this Agent agent, string name, string description)
        => new(agent, name, description);
}
