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
        var textDeltas = new[]
        {
            new TextDelta("Here is C# code: List<AccumulatedToolCall> _toolCalls = new();\n"),
            new TextDelta("public class AccumulatedToolCall [0] { public string Id { get; set; } }\n"),
            new TextDelta("var value = dict[\"key\"]; // [bold red] unescaped markup test\n")
        };

        var exception = Record.Exception(() =>
        {
            foreach (var delta in textDeltas)
            {
                renderer.Write(delta);
            }
            renderer.Complete();
        });

        Assert.Null(exception);
    }
}
