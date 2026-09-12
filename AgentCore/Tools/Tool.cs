using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging; 

namespace AgentCore.Tools; 

public sealed record ToolDefinition(string Name, string Description, JsonSchema ParametersSchema);
public interface ITool
{
    ToolDefinition Definition { get; }
    IAsyncEnumerable<IBlockEvent> InvokeStreamingAsync(JsonObject arguments, CancellationToken ct = default);
}

public static class ToolingBuilderExtensions
{
    public static AgentBuilder WithTooling(
        this AgentBuilder builder,
        bool parallel = true,
        int? maxConcurrency = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithTooling((tools, lf) => new Tooling(
            tools: tools,
            logger: lf.CreateLogger<Tooling>(),
            parallel: parallel,
            maxConcurrency: maxConcurrency,
            timeout: timeout
        ));
    }
}