using System.Text.Json.Serialization;
using AgentCore.LLM;
using AgentCore.LLM.Chat.Builders;

namespace AgentCore.LLM.Chat;

public class Message(Role role, IReadOnlyList<IContent>? contents = null)
{
    private readonly List<IContent> _contents = contents != null ? [.. contents] : [];
    private readonly IContentAssembler _assembler = new ContentAssembler();

    [JsonPropertyName("role")]
    public Role Role { get; } = role;

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    public Message(Role role, IContent content) : this(role, [content]) { }
    public Message(Role role, params IContent[] contents) : this(role, (IReadOnlyList<IContent>)contents) { }
    /// <summary>
    /// Ingests a streaming chunk, committing and yielding any completed <see cref="IContent"/> items.
    /// </summary>
    public IEnumerable<IContent> Receive(StreamChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        foreach (var item in _assembler.Receive(chunk))
        {
            _contents.Add(item);
            yield return item;
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }







