using System.Threading.Tasks;

namespace AgentCore.Host.Abstractions;

public interface IVsToolDispatcher
{
    Task<string> InvokeAsync(string tool, object toolArgs);
    bool TryHandleResult(string id, string? result, bool isError);
}
