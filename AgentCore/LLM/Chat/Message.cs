using System.Text.Json.Serialization;
using AgentCore.LLM;
using AgentCore.LLM.Chat.Builders;

namespace AgentCore.LLM.Chat;

public class Message
{
    private readonly List<IContent> _contents = new();
    private IContentBuilder? _activeBuilder;

    [JsonPropertyName("role")]
    public Role Role { get; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    [JsonConstructor]
    public Message(Role role, IReadOnlyList<IContent>? contents = null)
    {
        Role = role;
        if (contents != null)
        {
            _contents.AddRange(contents);
        }
    }

    /// <summary>
    /// Appends content directly to the message.
    /// </summary>
    public Message AddContent(IContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _contents.Add(content);
        return this;
    }

    /// <summary>
    /// Appends multiple settled content items directly to the message.
    /// </summary>
    public Message AddContents(IEnumerable<IContent> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        _contents.AddRange(contents);
        return this;
    }

    /// <summary>
    /// Feeds a streaming delta into the active content builder,
    /// committing and returning any settled <see cref="IContent"/> items.
    /// </summary>
    public IReadOnlyList<IContent> AddContentDelta(IContentDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        if (_activeBuilder == null || !_activeBuilder.CanHandle(delta))
        {
            _activeBuilder = ContentBuilderFactory.Create(delta);
        }

        var settled = _activeBuilder.Append(delta).ToList();
        if (settled.Count > 0)
        {
            _contents.AddRange(settled);
        }

        return settled;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }




