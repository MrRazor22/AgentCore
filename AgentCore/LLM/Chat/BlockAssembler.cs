using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

/// <summary>
/// Incremental chunk-to-message assembler that reconstructs content blocks and messages from stream events.
/// </summary>
public sealed class BlockAssembler
{
    private readonly Dictionary<int, (IBlockStartEvent Start, StringBuilder Buffer)> _blocks = [];
    private readonly Dictionary<int, IContent> _completed = [];
    private readonly List<int> _order = [];

    private Role _role = Role.Assistant;
    private string? _messageId;
    private string? _model;
    private string? _finishReason;
    private TokenUsage? _usage;

    public MessageMetadata Metadata => new(_messageId, _model, _finishReason, _usage);

    public void Push(IMessageEvent evt)
    {
        switch (evt)
        {
            case MessageStart s:
                _role = s.Role;
                _messageId = s.Id;
                _model = s.Model;
                break;

            case IBlockStartEvent s:
                _blocks[s.Index] = (s, new StringBuilder());
                _order.Add(s.Index);
                break;

            case TextDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Text);
                break;

            case ReasoningDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Thought);
                break;

            case ToolCallDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Arguments);
                break;

            case MessageEnd end:
                _finishReason = end.FinishReason;
                _usage = end.Usage;
                break;
        }
    }

    public IContent? CompleteBlock(int index)
    {
        if (_completed.TryGetValue(index, out var existing)) return existing;
        if (!_blocks.TryGetValue(index, out var block)) return null;

        IContent content = block.Start switch
        {
            TextStart => new Text(block.Buffer.ToString()),
            ReasoningStart => new Reasoning(block.Buffer.ToString()),
            ToolCallStart tc => new ToolCall(tc.Id, tc.Name, ParseArgs(block.Buffer.ToString())),
            _ => new Text(block.Buffer.ToString())
        };

        _completed[index] = content;
        return content;
    }

    private static JsonObject ParseArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        return JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
    }

    public Message ToMessage()
    {
        var contents = _order
            .Select(i => _completed.TryGetValue(i, out var c) ? c : CompleteBlock(i))
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();

        return new Message(_role, contents, Metadata);
    }
}
