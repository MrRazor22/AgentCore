using AgentCore.LLM;

namespace AgentCore.LLM.Chat; 

public enum Role { System, Assistant, User, Tool }

public interface IContent : IAgentEvent;

public interface IMetadata;

public sealed record MessageMetadata(
    string? Id = null,
    string? Model = null,
    string? FinishReason = null,
    TokenUsage? Usage = null
) : IMetadata;

public sealed record SummaryMetadata(
    int SummarizedCount = 0
) : IMetadata;

public class Message(
    Role role,
    IReadOnlyList<IContent>? contents = null,
    IReadOnlyList<IMetadata>? metadata = null,
    Guid? id = null)
{
    public Guid Id { get; init; } = id ?? Guid.NewGuid();
    protected readonly List<IContent> _contents = contents != null ? [.. contents] : [];

    public Message(Role role, IReadOnlyList<IContent>? contents, MessageMetadata? metadata)
        : this(role, contents, metadata != null ? [metadata] : null) { }

    public Role Role { get; protected set; } = role;
    public IReadOnlyList<IContent> Contents => _contents;
    public IReadOnlyList<IMetadata> Metadata { get; set; } = metadata ?? [];

    public void Append(IContent content) => _contents.Add(content);

    public T? Get<T>() where T : class, IMetadata =>
        Metadata.OfType<T>().FirstOrDefault();
}
