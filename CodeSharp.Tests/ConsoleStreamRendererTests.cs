using AgentCore.LLM;
using AgentCore.LLM.Chat;
using CodeSharp.UI;
using Xunit;

namespace CodeSharp.Tests;

public class ConsoleStreamRendererTests
{
    [Fact]
    public void Write_TextDeltaWithUnescapedSpectreMarkupBrackets_DoesNotThrowException()
    {
        var renderer = new ConsoleStreamRenderer(new GenericFallbackToolFormatter());

        // Deliberately test text containing square brackets and indexer patterns that crash raw AnsiConsole.Write()
        var textContents = new IContent[]
        {
            new Text("Here is C# code: List<AccumulatedToolCall> _toolCalls = new();\n"),
            new Text("public class AccumulatedToolCall [0] { public string Id { get; set; } }\n"),
            new Text("var value = dict[\"key\"]; // [bold red] unescaped markup test\n")
        };

        var exception = Record.Exception(() =>
        {
            foreach (var content in textContents)
            {
                renderer.Write(content);
            }
            renderer.Complete();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Write_ToolCallStreamingEvents_DoesNotThrowException()
    {
        var renderer = new ConsoleStreamRenderer(new GenericFallbackToolFormatter());

        var events = new object[]
        {
            new ToolCallStart(0, "call_123", "RunCommand"),
            new ToolCallDelta(0, "{\"commandLine\":"),
            new ToolCallDelta(0, " \"Get-Process\"}"),
            new ToolCallEnd(0)
        };

        var exception = Record.Exception(() =>
        {
            foreach (var evt in events)
            {
                renderer.Write(evt);
            }
            renderer.Complete();
        });

        Assert.Null(exception);
    }
}
