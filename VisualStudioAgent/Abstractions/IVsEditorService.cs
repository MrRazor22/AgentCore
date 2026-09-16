using System.Threading.Tasks;

namespace VisualStudioAgent.Abstractions;

public interface IVsEditorService
{
    Task<string> ReadFileAsync(string path, int startLine, int endLine);
    Task<string> EditFileAsync(string path, string targetContent, string replacementContent);
}
