using System.Text;

namespace AgentCore.Memory;

public interface IMemory
{
    Task RememberAsync(string content, CancellationToken ct = default);
    Task<string> RecallAsync(string query, CancellationToken ct = default);
}

public sealed class InMemoryMemoryProvider : IMemory
{
    private readonly StringBuilder _store = new();

    public Task RememberAsync(string content, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            lock (_store)
            {
                if (_store.Length > 0)
                {
                    _store.AppendLine();
                }
                _store.Append(content);
            }
        }
        return Task.CompletedTask;
    }

    public Task<string> RecallAsync(string query, CancellationToken ct = default)
    {
        lock (_store)
        {
            return Task.FromResult(_store.ToString());
        }
    }
}
