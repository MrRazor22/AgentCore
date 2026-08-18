using System.Text.Json.Serialization;
using AgentCore.LLM;
using AgentCore.LLM.Chat.Builders;

namespace AgentCore.LLM.Chat;

public class Message(Role role, IReadOnlyList<IContent>? contents = null)
{
    private readonly List<IContent> _contents = contents != null ? [.. contents] : [];
    private IContentBuilder? _activeBuilder;

    [JsonPropertyName("role")]
    public Role Role { get; } = role;

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    public Message(Role role, IContent content) : this(role, [content]) { }
    public Message(Role role, params IContent[] contents) : this(role, (IReadOnlyList<IContent>)contents) { }

    /// <summary>
    /// Appends content to the message, automatically consolidating with adjacent compatible content items.
    /// </summary>
    public Message AddContent(IContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (_contents.Count > 0 && _contents[^1].CanConsolidateWith(content))
        {
            _contents[^1] = _contents[^1].Consolidate(content);
        }
        else
        {
            _contents.Add(content);
        }

        return this;
    }

    /// <summary>
    /// Appends multiple content items to the message.
    /// </summary>
    public Message AddContents(IEnumerable<IContent> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        foreach (var content in contents)
        {
            AddContent(content);
        }
        return this;
    }

    /// <summary>
    /// Ingests a streaming delta, returning the real-time <see cref="IContent"/> chunk immediately
    /// while maintaining a clean, consolidated internal representation.
    /// </summary>
    public IReadOnlyList<IContent> Receive(IContentDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        if (_activeBuilder == null || !_activeBuilder.CanHandle(delta))
        {
            _activeBuilder = ContentBuilderFactory.Create(delta);
        }

        var settled = _activeBuilder.Append(delta).ToList();
        foreach (var item in settled)
        {
            AddContent(item);
        }

        return settled;
    }




}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }





