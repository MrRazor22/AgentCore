using System.Runtime.CompilerServices;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat; 

public enum Role { System, Assistant, User, Tool }

public interface IContent 
{ 
    string Type => GetType().Name;
    int EstimateTokens();
    IContent Truncate(int maxTokens, string? notice = null);
} 

public interface IMetadata
{
    string Type => GetType().Name;
}

public sealed record MessageMetadata(
    string? Id = null,
    string? Model = null,
    string? FinishReason = null,
    TokenUsage? Usage = null
) : IMetadata;

public class Message(
    Role role,
    IReadOnlyList<IContent>? contents = null,
    IReadOnlyList<IMetadata>? metadata = null)
{
    protected readonly List<IContent> _contents = contents != null ? [.. contents] : [];

    public Message(Role role, IReadOnlyList<IContent>? contents, MessageMetadata? metadata)
        : this(role, contents, metadata != null ? [metadata] : null) { }

    public Role Role { get; protected set; } = role;
    public IReadOnlyList<IContent> Contents => _contents;
    public IReadOnlyList<IMetadata> Metadata { get; set; } = metadata ?? [];
}

public static class MetadataExtensions
{
    public static T? Get<T>(this IEnumerable<IMetadata>? metadata) where T : class, IMetadata =>
        metadata?.OfType<T>().FirstOrDefault();

    public static IEnumerable<T> GetAll<T>(this IEnumerable<IMetadata>? metadata) where T : class, IMetadata =>
        metadata?.OfType<T>() ?? [];

    public static T? Get<T>(this Message message) where T : class, IMetadata =>
        message.Metadata.Get<T>();

    public static IEnumerable<T> GetAll<T>(this Message message) where T : class, IMetadata =>
        message.Metadata.GetAll<T>();
}



