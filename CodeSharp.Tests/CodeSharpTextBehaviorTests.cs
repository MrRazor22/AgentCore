using System;
using System.IO;
using System.Linq;
using Xunit;
using CodeSharp;
using AgentCore.LLM.Chat;

namespace CodeSharp.Tests;

public class CodeSharpTextBehaviorTests : IDisposable
{
    private readonly string _testDir;
    private readonly CodeSharpTextBehavior _behavior;

    public CodeSharpTextBehaviorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "codesharp_behavior_test_" + Guid.NewGuid().ToString("N"));
        _behavior = new CodeSharpTextBehavior(_testDir);
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
        var text = new Text("Short text");
        var truncated = _behavior.Truncate(text, 100);

        Assert.Same(text, truncated);
        Assert.False(Directory.Exists(_testDir));
    }

    [Fact]
    public void Truncate_OverBudget_SpillsUntruncatedContentToDiskAndAttachesNotice()
    {
        string bigText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}: Some long output content here..."));
        var text = new Text(bigText);

        var truncated = _behavior.Truncate(text, 50);

        Assert.NotSame(text, truncated);

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
