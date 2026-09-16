using System;
using System.Threading.Tasks;

namespace VisualStudioAgent.Abstractions;

public interface IHostProcessManager : IDisposable
{
    bool IsRunning { get; }
    Task StartAsync(Func<string, Task> onMessageReceived);
    Task SendAsync(string message);
    void Stop();
}
