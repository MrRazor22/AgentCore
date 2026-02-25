using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Chat;

public interface IContent { }

public sealed class TextContent(string Text) : IContent
{
    public string Text { get; } = Text;
    public static implicit operator TextContent(string text) => new(text);
}

public class ToolCall : IContent
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; private set; }
    [JsonPropertyName("arguments")] public JsonObject Arguments { get; private set; }
    [JsonIgnore] public object[] Parameters { get; private set; } = [];
    [JsonIgnore] public string? Message { get; set; }

    [JsonConstructor]
    public ToolCall(string id, string name, JsonObject arguments, object[]? parameters = null, string? message = null)
    {
        Id = id;
        Name = name;
        Arguments = arguments ?? new JsonObject();
        Parameters = parameters ?? [];
        Message = message;
    }

    public ToolCall(string message) : this(Guid.NewGuid().ToString(), "", new JsonObject()) => Message = message;

    public static ToolCall Empty { get; } = new(Guid.NewGuid().ToString(), "", new JsonObject());

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Message);

    public override string ToString()
    {
        var argsStr = Arguments?.Count > 0
            ? string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"))
            : "none";
        return $"Name: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
    }
}

public sealed class ToolCallResult(ToolCall Call, object? Result) : IContent
{
    public ToolCall Call { get; } = Call;
    public object? Result { get; } = Result;
}
