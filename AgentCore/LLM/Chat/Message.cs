using System.Text.Json.Serialization;
using AgentCore.LLM;
using AgentCore.LLM.Chat.Builders;

namespace AgentCore.LLM.Chat;

public class Message
{
    private IContentBuilder? _activeBuilder;
    private readonly List<IContent> _contents = new();

    [JsonPropertyName("role")]
    public Role Role { get; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents =>
        _activeBuilder is null
            ? _contents
            : [.. _contents, .. _activeBuilder.ToContents()];

    [JsonConstructor]
    public Message(Role role, IReadOnlyList<IContent>? contents = null)
    {
        Role = role;
        _contents.AddRange(contents ?? []);
    }

    /// <summary>
    /// Appends content directly to the message, committing any active streaming content builder first.
    /// </summary>
    /// <param name="content">The settled content item to add.</param>
    /// <returns>The current <see cref="Message"/> instance for fluent chaining.</returns>
    public Message AddContent(IContent content)
    {
        ArgumentNullException.ThrowIfNull(content); 
        CommitActiveContent();
        _contents.Add(content);
        return this;
    }

    /// <summary>
    /// Appends a streaming delta to the active content builder.
    /// </summary>
    /// <param name="delta">The incoming streaming delta.</param>
    /// <returns>The current <see cref="Message"/> instance for fluent chaining.</returns>
    public Message AddContentDelta(IContentDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        if (_activeBuilder?.TryAppend(delta) != true)
        {
            CommitActiveContent();
            _activeBuilder = ContentBuilderFactory.Create(delta);
            _activeBuilder.TryAppend(delta);
        }

        return this;
    }

    private void CommitActiveContent()
    {
        if (_activeBuilder is null) return;
        _contents.AddRange(_activeBuilder.ToContents());
        _activeBuilder = null;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }
