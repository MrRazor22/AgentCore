using System.Text.Json.Serialization;
using AgentCore.LLM;
using AgentCore.LLM.Chat.Builders;

namespace AgentCore.LLM.Chat;

public class Message(Role role, IReadOnlyList<IContent>? contents = null)
{
    private readonly List<IContent> _contents = contents != null ? [.. contents] : [];
    private readonly IContentBuilder _builder = new CompositeContentBuilder();

    [JsonPropertyName("role")]
    public Role Role { get; } = role;

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    public Message(Role role, IContent content) : this(role, [content]) { }
    public Message(Role role, params IContent[] contents) : this(role, (IReadOnlyList<IContent>)contents) { }
    /// <summary>
    /// Ingests a streaming delta, committing and yielding any completed <see cref="IContent"/> items.
    /// </summary>
    public IEnumerable<IContent> Receive(IContentDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        foreach (var item in _builder.Append(delta))
        {
            _contents.Add(item);
            yield return item;
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }







