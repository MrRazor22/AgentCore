using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentCore.Chat;

public interface IChatContent { }

public sealed class TextContent(string Text) : IChatContent
{
    public string Text { get; } = Text;
    public static implicit operator TextContent(string text) => new(text);
}

public class ToolCall : IChatContent
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("name")] public string Name { get; private set; }
    [JsonProperty("arguments")] public JObject Arguments { get; private set; }
    [JsonIgnore] public object[] Parameters { get; private set; } = [];
    [JsonIgnore] public string? Message { get; set; }

    [JsonConstructor]
    public ToolCall(string id, string name, JObject arguments, object[]? parameters = null, string? message = null)
    {
        Id = id;
        Name = name;
        Arguments = arguments ?? new JObject();
        Parameters = parameters ?? [];
        Message = message;
    }

    public ToolCall(string message) : this(Guid.NewGuid().ToString(), "", new JObject()) => Message = message;

    public static ToolCall Empty { get; } = new(Guid.NewGuid().ToString(), "", new JObject());

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Message);

    public override string ToString()
    {
        var argsStr = Arguments?.Count > 0
            ? string.Join(", ", Arguments.Properties().Select(p => $"{p.Name}: {p.Value}"))
            : "none";
        return $"Name: '{Name}' (id: {Id}) with Arguments: [{argsStr}]";
    }
}

public sealed class ToolCallResult(ToolCall Call, object? Result) : IChatContent
{
    public ToolCall Call { get; } = Call;
    public object? Result { get; } = Result;
}
