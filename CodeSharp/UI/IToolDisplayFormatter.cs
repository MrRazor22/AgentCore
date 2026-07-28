using AgentCore.LLM.Chat;

namespace CodeSharp.UI;

public interface IToolDisplayFormatter
{
    bool CanFormat(string toolName);
    ToolDisplayInfo FormatCall(ToolCall call);
    string FormatResult(string rawResult);
}
