using System.Threading.Tasks;

namespace VisualStudioAgent.Abstractions;

public interface IVsTerminalService
{
    Task<(string Result, bool IsError)> RunCommandAsync(string commandLine, string workingDirectory, int maxCharacters);
}
