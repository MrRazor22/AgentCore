using System.Text.Json.Serialization;

namespace AgentCore.Conversation;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }
