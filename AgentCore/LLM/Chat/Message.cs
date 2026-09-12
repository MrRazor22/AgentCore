using AgentCore.LLM;

namespace AgentCore.LLM.Chat; 

public enum Role { System, Assistant, User, Tool }

public interface IHasMetadata { IReadOnlyList<IMetadata> Metadata => []; }
public interface IContent : IHasMetadata { string? Id => null; }
public interface IMetadata;

public sealed record MessageMetadata(string? Id = null, string? Model = null, string? FinishReason = null, TokenUsage? Usage = null) : IMetadata;
public sealed record SummaryMetadata(int SummarizedCount = 0) : IMetadata;
public sealed record ErrorMetadata(string Message, string? Code = null) : IMetadata;
public sealed record ToolMetadata(string CallId, string? ToolName = null) : IMetadata;

public class Message(
    Role role,
    IReadOnlyList<IContent>? contents = null,
    IReadOnlyList<IMetadata>? metadata = null,
    string? id = null) : IAgentEvent, IHasMetadata
{
    public Role Role { get; } = role;
    public string? Id { get; } = id ?? metadata?.OfType<MessageMetadata>().FirstOrDefault()?.Id;
    public IReadOnlyList<IContent> Contents { get; } = contents ?? [];
    public IReadOnlyList<IMetadata> Metadata { get; } = metadata ?? [];

    public Message(Role role, IReadOnlyList<IContent>? contents, MessageMetadata? metadata)
        : this(role, contents, metadata != null ? [metadata] : null) { }
}

public static class MetadataExtensions
{
    public static T? Get<T>(this IHasMetadata target) where T : class, IMetadata => target.Metadata.OfType<T>().FirstOrDefault();
    public static T? Get<T>(this IEnumerable<IMetadata>? metadata) where T : class, IMetadata => metadata?.OfType<T>().FirstOrDefault();
}
