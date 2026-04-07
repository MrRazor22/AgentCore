using System.Text.Json.Nodes;
using AgentCore.Conversation;
using AgentCore.Tooling;

namespace AgentCore.CodingAgent;

public interface ICSharpExecutor : IDisposable
{
    void SendTools(IReadOnlyList<Tool> tools, IToolExecutor executor);
    void SendVariables(Dictionary<string, object?> variables);
    CodeOutput Execute(string codeAction);
}
