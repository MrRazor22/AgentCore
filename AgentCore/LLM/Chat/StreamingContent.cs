using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AgentCore.LLM.Chat;

public interface IStreamingContent : IContent
{
    IContent ToContent();
}

public interface IStreamingContent<out TContent> : IStreamingContent where TContent : IContent
{
    new TContent ToContent();
}

public sealed class StreamingText(ChannelReader<TextDelta> reader, StringBuilder sb)
    : IStreamingContent<Text>, IAsyncEnumerable<TextDelta>
{
    public IAsyncEnumerator<TextDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public Text ToContent() => new(sb.ToString());
    IContent IStreamingContent.ToContent() => ToContent();

    public int EstimateTokens() => (int)Math.Ceiling(sb.Length / 4.0);
    public IContent Truncate(int maxTokens, string? notice = null) => ToContent().Truncate(maxTokens, notice);
    public override string ToString() => sb.ToString();
}

public sealed class StreamingReasoning(ChannelReader<ReasoningDelta> reader, StringBuilder sb)
    : IStreamingContent<Reasoning>, IAsyncEnumerable<ReasoningDelta>
{
    public IAsyncEnumerator<ReasoningDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public Reasoning ToContent() => new(sb.ToString());
    IContent IStreamingContent.ToContent() => ToContent();

    public int EstimateTokens() => (int)Math.Ceiling(sb.Length / 4.0);
    public IContent Truncate(int maxTokens, string? notice = null) => ToContent().Truncate(maxTokens, notice);
    public override string ToString() => sb.ToString();
}

public sealed class StreamingToolCall(string id, string name, ChannelReader<ToolCallDelta> reader, StringBuilder args)
    : IStreamingContent<ToolCall>, IAsyncEnumerable<ToolCallDelta>
{
    public string Id => id;
    public string Name => name;

    public IAsyncEnumerator<ToolCallDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public ToolCall ToContent()
    {
        var raw = args.ToString();
        JsonObject? parsedArgs = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                parsedArgs = JsonNode.Parse(raw)?.AsObject();
            }
            catch (Exception ex)
            {
                throw new FormatException($"Malformed JSON arguments for tool '{name}' (id: '{id}'): {raw}", ex);
            }
        }

        return new ToolCall(id, name, parsedArgs ?? new JsonObject());
    }

    IContent IStreamingContent.ToContent() => ToContent();

    public int EstimateTokens() => (int)Math.Ceiling((name.Length + args.Length) / 4.0);
    public IContent Truncate(int maxTokens, string? notice = null) => ToContent().Truncate(maxTokens, notice);
    public override string ToString() => $"{name}({args})";
}