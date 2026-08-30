using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat;

public sealed record MessageMetadata(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null,
    [property: JsonPropertyName("usage")] TokenUsage? Usage = null
);

[JsonConverter(typeof(MessageJsonConverter))]
public class Message : IAsyncEnumerable<IContent>
{
    private readonly IAsyncEnumerable<IMessageEvent>? _stream;
    private readonly List<IContent> _contents = [];
    private int _consumed;

    [JsonPropertyName("role")]
    public Role Role { get; private set; }

    [JsonPropertyName("contents")]
    public IReadOnlyList<IContent> Contents => _contents;

    [JsonPropertyName("metadata")]
    public MessageMetadata? Metadata { get; private set; }

    public Message(Role role, IReadOnlyList<IContent>? contents = null, MessageMetadata? metadata = null)
    {
        Role = role;
        if (contents != null)
        {
            _contents.AddRange(contents);
        }
        Metadata = metadata;
    }

    public Message(Role role, IContent content) : this(role, [content], null) { }
    public Message(Role role, params IContent[] contents) : this(role, (IReadOnlyList<IContent>)contents, null) { }

    public Message(IAsyncEnumerable<IMessageEvent> stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Role = Role.Assistant;
    }

    public async IAsyncEnumerator<IContent> GetAsyncEnumerator(CancellationToken ct = default)
    {
        if (_stream == null)
        {
            foreach (var content in _contents)
            {
                yield return content;
            }
            yield break;
        }

        if (Interlocked.Exchange(ref _consumed, 1) != 0)
        {
            throw new InvalidOperationException("A streaming Message can only be consumed once.");
        }

        var activeBlocks = new Dictionary<int, IContentAccumulator>();
        var completedBlocks = new SortedDictionary<int, IContent>();

        string? id = null;
        string? model = null;
        string? finishReason = null;
        TokenUsage? usage = null;

        await foreach (var evt in _stream.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case MessageStart s:
                    Role = s.Role;
                    id = s.Id;
                    model = s.Model;
                    break;

                case TextContentStart s:
                    AssertCanStart(activeBlocks, completedBlocks, s.Index);
                    activeBlocks[s.Index] = new TextAccumulator();
                    break;
                case TextContentDelta d:
                    if (activeBlocks.TryGetValue(d.Index, out var tAcc) && tAcc is TextAccumulator tb)
                    {
                        tb.Append(d.Text);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Received TextContentDelta for unstarted block at index {d.Index}.");
                    }
                    break;
                case TextContentEnd e:
                    if (activeBlocks.Remove(e.Index, out var endingTBlock))
                    {
                        var text = endingTBlock.Complete();
                        completedBlocks[e.Index] = text;
                        yield return text;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Received TextContentEnd for unstarted block at index {e.Index}.");
                    }
                    break;

                case ReasoningContentStart s:
                    AssertCanStart(activeBlocks, completedBlocks, s.Index);
                    activeBlocks[s.Index] = new ReasoningAccumulator();
                    break;
                case ReasoningContentDelta d:
                    if (activeBlocks.TryGetValue(d.Index, out var rAcc) && rAcc is ReasoningAccumulator rb)
                    {
                        rb.Append(d.Thought);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Received ReasoningContentDelta for unstarted block at index {d.Index}.");
                    }
                    break;
                case ReasoningContentEnd e:
                    if (activeBlocks.Remove(e.Index, out var endingRBlock))
                    {
                        var reasoning = endingRBlock.Complete();
                        completedBlocks[e.Index] = reasoning;
                        yield return reasoning;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Received ReasoningContentEnd for unstarted block at index {e.Index}.");
                    }
                    break;

                case ToolCallContentStart s:
                    AssertCanStart(activeBlocks, completedBlocks, s.Index);
                    activeBlocks[s.Index] = new ToolCallAccumulator(s.Id, s.Name, s.Index);
                    break;
                case ToolCallContentDelta d:
                    if (activeBlocks.TryGetValue(d.Index, out var tcAcc) && tcAcc is ToolCallAccumulator tcb)
                    {
                        tcb.Append(d.Arguments);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Received ToolCallContentDelta for unstarted block at index {d.Index}.");
                    }
                    break;
                case ToolCallContentEnd e:
                    if (activeBlocks.Remove(e.Index, out var endingTcBlock))
                    {
                        var toolCall = endingTcBlock.Complete();
                        completedBlocks[e.Index] = toolCall;
                        yield return toolCall;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Received ToolCallContentEnd for unstarted block at index {e.Index}.");
                    }
                    break;

                case MessageEnd end:
                    if (activeBlocks.Count > 0)
                    {
                        throw new InvalidOperationException($"Message stream ended unexpectedly with {activeBlocks.Count} unclosed content block(s).");
                    }
                    finishReason = end.FinishReason;
                    usage = end.Usage;
                    break;
            }
        }

        _contents.Clear();
        _contents.AddRange(completedBlocks.Values);

        if (id != null || model != null || finishReason != null || usage != null)
        {
            Metadata = new MessageMetadata(id, model, finishReason, usage);
        }
    }

    private static void AssertCanStart(
        Dictionary<int, IContentAccumulator> active,
        SortedDictionary<int, IContent> completed,
        int index)
    {
        if (active.ContainsKey(index) || completed.ContainsKey(index))
        {
            throw new InvalidOperationException($"Content block at index {index} has already been started.");
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }

public sealed class MessageJsonConverter : JsonConverter<Message>
{
    public override Message? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var role = root.TryGetProperty("role", out var rProp)
            ? rProp.Deserialize<Role>(options)
            : Role.User;

        var contents = root.TryGetProperty("contents", out var cProp)
            ? cProp.Deserialize<List<IContent>>(options)
            : null;

        var metadata = root.TryGetProperty("metadata", out var mProp)
            ? mProp.Deserialize<MessageMetadata>(options)
            : null;

        return new Message(role, contents, metadata);
    }

    public override void Write(Utf8JsonWriter writer, Message value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("role");
        JsonSerializer.Serialize(writer, value.Role, options);

        writer.WritePropertyName("contents");
        JsonSerializer.Serialize(writer, value.Contents, options);

        if (value.Metadata != null)
        {
            writer.WritePropertyName("metadata");
            JsonSerializer.Serialize(writer, value.Metadata, options);
        }

        writer.WriteEndObject();
    }
}

