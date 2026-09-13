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
    MessageMetadata? info = null,
    IReadOnlyList<IMetadata>? metadata = null) : IHasMetadata
{
    public Role Role { get; } = role;
    public IReadOnlyList<IContent> Contents { get; } = contents ?? [];
    public MessageMetadata Info { get; } = info ?? new();
    public string? Id => Info.Id;
    public IReadOnlyList<IMetadata> Metadata { get; } = metadata ?? [];

    public Message(Role role, IReadOnlyList<IContent>? contents, IReadOnlyList<IMetadata>? metadata)
        : this(
            role,
            contents,
            metadata?.OfType<MessageMetadata>().FirstOrDefault(),
            metadata?.Where(m => m is not MessageMetadata).ToList()) { }
}

public static class MetadataExtensions
{
    public static T? Get<T>(this IHasMetadata target) where T : class, IMetadata => target.Metadata.OfType<T>().FirstOrDefault();
    public static T? Get<T>(this IEnumerable<IMetadata>? metadata) where T : class, IMetadata => metadata?.OfType<T>().FirstOrDefault();
}
