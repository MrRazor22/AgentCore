using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

/// <summary>
/// A live Message that incrementally reconstructs itself and its contents from LLM stream events.
/// </summary>
public sealed class StreamingMessage(Role role = Role.Assistant) : Message(role)
{
    private readonly SortedDictionary<int, (IBlockStartEvent Start, StringBuilder Buffer)> _blocks = [];

    /// <summary>
    /// Ingests a streaming event. If the event completes a content block, the block is appended
    /// to Contents and returned immediately; otherwise returns null.
    /// </summary>
    public IContent? Push(IMessageEvent evt)
    {
        switch (evt)
        {
            case MessageStart s:
                Role = s.Role;
                Metadata = [new MessageMetadata(s.Id, s.Model)];
                return null;

            case IBlockStartEvent s:
                _blocks[s.Index] = (s, new StringBuilder());
                return null;

            case TextDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Text);
                return null;

            case ReasoningDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Thought);
                return null;

            case ToolCallDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Arguments);
                return null;

            case IBlockEndEvent end:
                return CompleteBlock(end.Index);

            case MessageEnd end:
                var current = Get<MessageMetadata>();
                Metadata = [new MessageMetadata(current?.Id, current?.Model, end.FinishReason, end.Usage)];
                return null;

            default:
                return null;
        }
    }

    public IContent? CompleteBlock(int index)
    {
        if (!_blocks.Remove(index, out var b)) return null;

        IContent content = b.Start switch
        {
            TextStart => new Text(b.Buffer.ToString()),
            ReasoningStart => new Reasoning(b.Buffer.ToString()),
            ToolCallStart tc => new ToolCall(tc.Id, tc.Name, ParseArgs(b.Buffer.ToString())),
            _ => new Text(b.Buffer.ToString())
        };

        _contents.Add(content);
        return content;
    }

    private static JsonObject ParseArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        return JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
    }
}
