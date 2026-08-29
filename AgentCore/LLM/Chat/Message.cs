using System.Text.Json.Serialization;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat;

public class Message(Role role, IReadOnlyList<IContent>? contents = null)
{
    private readonly List<IContent> _contents = contents != null ? [.. contents] : [];
    private readonly ContentAssembler _assembler = new();

    [JsonPropertyName("role")]
    public Role Role { get; } = role;

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    public Message(Role role, IContent content) : this(role, [content]) { }
    public Message(Role role, params IContent[] contents) : this(role, (IReadOnlyList<IContent>)contents) { }

    /// <summary>
    /// Ingests a streaming lifecycle event, committing and returning any completed <see cref="IContent"/> items.
    /// </summary>
    public IReadOnlyList<IContent> Receive(ILLMOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var completed = _assembler.Receive(output);
        if (completed.Count > 0)
        {
            _contents.AddRange(completed);
        }
        return completed;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }
