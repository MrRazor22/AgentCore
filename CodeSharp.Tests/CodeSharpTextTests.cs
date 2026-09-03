using System;
using System.IO;
using Xunit;
using CodeSharp;
using AgentCore.LLM.Chat;

namespace CodeSharp.Tests;

public class CodeSharpTextTests : IDisposable
{
    private readonly string _testDir;

    public CodeSharpTextTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "codesharp_text_test_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public void Truncate_WithinBudget_ReturnsSameInstanceWithoutSpillover()
    {
        var text = new CodeSharpText("Short text", _testDir);
        var truncated = text.Truncate(100);

        Assert.Same(text, truncated);
        Assert.False(Directory.Exists(_testDir));
    }

    [Fact]
    public void Truncate_OverBudget_SpillsUntruncatedContentToDiskAndAttachesNotice()
    {
        string bigText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}: Some long output content here..."));
        var text = new CodeSharpText(bigText, _testDir);

        var truncated = text.Truncate(50);

        Assert.NotSame(text, truncated);
        Assert.IsType<CodeSharpText>(truncated);

        string output = truncated.ToString();
        Assert.Contains("Output truncated", output);
        Assert.Contains("Total lines: 100", output);
        Assert.Contains(".log", output);

        // Verify the spillover file was created and contains the full text
        var files = Directory.GetFiles(_testDir, "*.log");
        Assert.Single(files);
        string saved = File.ReadAllText(files[0]);
        Assert.Equal(bigText, saved);
    }
}
