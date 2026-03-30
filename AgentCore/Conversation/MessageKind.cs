using System;
using System.Text.Json.Serialization;

namespace AgentCore.Conversation;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MessageKind
{
    Default = 0,
    Synthetic = 1 << 0,
    Summary = 1 << 1,
}
