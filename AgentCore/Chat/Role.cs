using System.Text.Json.Serialization;

namespace AgentCore.Chat;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Role { System, Assistant, User, Tool }
