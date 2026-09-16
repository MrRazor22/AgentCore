using System.Threading.Tasks;

namespace VisualStudioAgent.Abstractions;

public interface IVsSearchService
{
    Task<string> SearchAsync(string? targetPath, string? query, string? include, bool isRegex, bool caseSensitive);
}
