using System;
using System.IO;
using CodeSharp.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class SearchToolTests : IDisposable
{
    private readonly string _tempWorkspace;

    public SearchToolTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "SearchToolTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempWorkspace);
        SearchTool.Initialize(_tempWorkspace);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void Search_WorkspaceContainment_ThrowsExceptionOnEscape()
    {
        Assert.Throws<System.Security.SecurityException>(() =>
        {
            SearchTool.Search(path: "../outside_dir", query: "test");
        });
    }

    [Fact]
    public void Search_NoMatchesFound_ReturnsNoMatchesMessage()
    {
        File.WriteAllText(Path.Combine(_tempWorkspace, "file.txt"), "hello world");
        
        var result = SearchTool.Search(query: "nonexistenttext");

        Assert.Contains("No matches found for", result);
    }

    [Fact]
    public void Search_FindsMatchAndReturnsRelativePath()
    {
        var subDir = Path.Combine(_tempWorkspace, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "target.txt"), "special target query phrase");

        var result = SearchTool.Search(query: "special target");

        // Should return relative path
        Assert.Contains("sub" + Path.DirectorySeparatorChar + "target.txt", result);
        Assert.Contains("special target query phrase", result);
    }

    [Fact]
    public void Search_IgnoresGitBinAndObjDirectories()
    {
        // 1. Setup git, bin, obj directories with matching file contents
        var gitDir = Path.Combine(_tempWorkspace, ".git");
        var binDir = Path.Combine(_tempWorkspace, "bin");
        var objDir = Path.Combine(_tempWorkspace, "obj");

        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(objDir);

        File.WriteAllText(Path.Combine(gitDir, "ignored.txt"), "secret_phrase");
        File.WriteAllText(Path.Combine(binDir, "ignored.txt"), "secret_phrase");
        File.WriteAllText(Path.Combine(objDir, "ignored.txt"), "secret_phrase");

        // 2. Setup a valid directory
        var srcDir = Path.Combine(_tempWorkspace, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "valid.txt"), "secret_phrase");

        var result = SearchTool.Search(query: "secret_phrase");

        // Should find in src but NOT in .git, bin, obj
        Assert.Contains("src" + Path.DirectorySeparatorChar + "valid.txt", result);
        Assert.DoesNotContain(".git", result);
        Assert.DoesNotContain("bin", result);
        Assert.DoesNotContain("obj", result);
    }
}
