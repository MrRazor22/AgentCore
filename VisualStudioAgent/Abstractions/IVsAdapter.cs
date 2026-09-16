using System.Text.Json;
using System.Threading.Tasks;

namespace VisualStudioAgent.Abstractions;

public interface IVsAdapter
{
    Task<(string Result, bool IsError)> DispatchAsync(string tool, JsonElement args);
}
