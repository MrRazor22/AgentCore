using System.Text.Json.Serialization;
using AgentCore.LLM;
using AgentCore.LLM.Chat.Builders;

namespace AgentCore.LLM.Chat;

public class Message
{
    private readonly List<IContent> _contents = new();
    private IContentBuilder? _activeBuilder;

    [JsonPropertyName("role")]
    public Role Role { get; set; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents
    {
        get => _contents;
        set
        {
            _contents.Clear();
            _contents.AddRange(value);
            _activeBuilder = null;
        }
    }

    public Message(Role role)
    {
        Role = role;
    }

    [JsonConstructor]
    public Message(Role role, IReadOnlyList<IContent> contents)
    {
        Role = role;
        _contents.AddRange(contents);
    }

    public Message(Role role, IContent content) : this(role, [content]) { }

    /// <summary>
    /// Appends a streaming delta to the active content builder, settling and returning completed content items when a boundary is reached.
    /// </summary>
    /// <param name="delta">The incoming streaming delta.</param>
    /// <returns>Any settled <see cref="IContent"/> item(s) that completed as a result of this delta.</returns>
    public IReadOnlyList<IContent> Append(IContentDelta delta)
    {
        IReadOnlyList<IContent> completed = Array.Empty<IContent>();

        if (_activeBuilder != null && !_activeBuilder.CanAccept(delta))
        {
            completed = FlushActiveBuilder();
        }

        _activeBuilder ??= ContentBuilderFactory.Create(delta);
        _activeBuilder.Append(delta);

        return completed;
    }

    /// <summary>
    /// Settles and returns all remaining active content items at the end of the stream.
    /// </summary>
    /// <returns>The final settled <see cref="IContent"/> item(s).</returns>
    public IReadOnlyList<IContent> Complete()
    {
        return FlushActiveBuilder();
    }

    private IReadOnlyList<IContent> FlushActiveBuilder()
    {
        if (_activeBuilder == null) return Array.Empty<IContent>();

        var built = _activeBuilder.Build();
        _contents.AddRange(built);
        _activeBuilder = null;
        return built;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }

public static class MessageExtensions
{
    public static T AddIfValid<T>(this T list, Role role, IContent? content) where T : ICollection<Message>
    {
        if (content is not null && (content is not Text t || !string.IsNullOrEmpty(t.Value))) list.Add(new Message(role, content));
        return list;
    }

    public static T AddIfValid<T>(this T list, Message? message) where T : ICollection<Message>
    {
        if (message?.Contents.Count > 0) list.Add(message);
        return list;
    }
}
