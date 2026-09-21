using AgentCore.LLM;

namespace AgentCore.LLM.Chat; 

public enum Role { System, Assistant, User, Tool }

public interface IContent : IContentEvent;

public interface IMetadata;

public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0, int TotalTokens = 0) : IMetadata;
public sealed record Summary(int CompactedMessages = 0, string? ThroughMessageId = null) : IMetadata;

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
}

public static class MetadataExtensions
{
    public static T? Get<T>(this Message message) where T : class, IMetadata => message.Metadata.OfType<T>().FirstOrDefault();
    public static T? Get<T>(this IEnumerable<IMetadata>? metadata) where T : class, IMetadata => metadata?.OfType<T>().FirstOrDefault();
}
