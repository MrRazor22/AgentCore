using System.Diagnostics;

namespace AgentCore.Diagnostics;

public static class AgentDiagnosticSource
{
    public static readonly ActivitySource Source = new("AgentCore");
}
