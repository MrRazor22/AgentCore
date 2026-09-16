using System.Text.Json;

namespace AgentCore.Host.Ipc;

public record IpcMessage(
    string Type,
    string? Id = null,
    string? Name = null,
    JsonElement? Args = null,
    string? Text = null,
    string? Result = null,
    bool Error = false,
    string[]? Todos = null);
