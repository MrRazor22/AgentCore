using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;
 
/// <summary>
/// Root interface for settled, fully validated semantic content items.
/// Streamed at the Agent boundary, stored in Message objects, and retained in IContext.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(ToolCall), "toolCall")]
[JsonDerivedType(typeof(ToolResult), "toolResult")]
[JsonDerivedType(typeof(Reasoning), "reasoning")]
public interface IContent
{
}

public interface ITruncatable
{
    IContent Truncate(int maxTokens);
}

public interface IEstimatable
{
    int EstimateTokens();
}


