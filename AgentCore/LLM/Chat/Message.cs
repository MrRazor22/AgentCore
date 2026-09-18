using System.Text.Json.Serialization;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat; 

public enum Role { System, Assistant, User, Tool }

public interface IContent : IContentEvent;

public interface IMetadata;

public sealed record ToolCallId(string Value) : IMetadata;
public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0, int TotalTokens = 0) : IMetadata;
public sealed record Summary(int Count = 0) : IMetadata;
public sealed record Interrupted(string Reason = "Interrupted") : IMetadata;

[method: JsonConstructor]
public class Message(
    Role role,
    IReadOnlyList<IContent>? contents = null,
    string? id = null,
    IReadOnlyList<IMetadata>? metadata = null) : IMessageEvent
{
    public Role Role { get; } = role;
    public IReadOnlyList<IContent> Contents { get; } = contents ?? [];
    public string? Id { get; } = id;
    public IReadOnlyList<IMetadata> Metadata { get; } = metadata ?? [];

    public Message(Role role, IReadOnlyList<IContent>? contents, IReadOnlyList<IMetadata>? metadata)
        : this(role, contents, null, metadata) { }
}

public static class MetadataExtensions
{
    public static T? Get<T>(this Message message) where T : class, IMetadata => message.Metadata.OfType<T>().FirstOrDefault();
    public static T? Get<T>(this IEnumerable<IMetadata>? metadata) where T : class, IMetadata => metadata?.OfType<T>().FirstOrDefault();
}
