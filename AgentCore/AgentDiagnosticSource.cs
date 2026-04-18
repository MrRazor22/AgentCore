using System.Diagnostics;

namespace AgentCore;

public static class AgentDiagnosticSource
{
    public static readonly ActivitySource Source = new("AgentCore");
}
