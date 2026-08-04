using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class MessageAccumulatorTests
{
    private static async IAsyncEnumerable<ILLMOutput> ToAsyncStream(IEnumerable<ILLMOutput> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AccumulateAsync_IndexAndIdPermutations_MergesCorrectly()
    {
        // RAW stream sequence 1: Index + ID -> later ID only
        var sequence1 = new List<ILLMOutput>
        {
            new ToolCallDelta("ABC", "RunCommand", "", 0),
            new ToolCallDelta("ABC", null, "{\"commandLine\":\"ls\"}", null)
        };

        var (message1, _, _) = await ToAsyncStream(sequence1).AccumulateAsync();
        Assert.NotNull(message1);
        var calls1 = message1.Contents.OfType<ToolCall>().ToList();
        Assert.Single(calls1);
        Assert.Equal("ABC", calls1[0].Id);
        Assert.Equal("RunCommand", calls1[0].Name);
        Assert.Contains("ls", calls1[0].Arguments.ToString());

        // RAW stream sequence 2: ID only -> later Index + ID
        var sequence2 = new List<ILLMOutput>
        {
            new ToolCallDelta("XYZ", "SearchWeb", "", null),
            new ToolCallDelta("XYZ", null, "{\"query\":\"test\"}", 1)
        };

        var (message2, _, _) = await ToAsyncStream(sequence2).AccumulateAsync();
        Assert.NotNull(message2);
        var calls2 = message2.Contents.OfType<ToolCall>().ToList();
        Assert.Single(calls2);
        Assert.Equal("XYZ", calls2[0].Id);
        Assert.Equal("SearchWeb", calls2[0].Name);
        Assert.Contains("test", calls2[0].Arguments.ToString());
    }

    [Fact]
    public async Task AccumulateAsync_IndexAndId_ReconcilesWithoutIdDuplication()
    {
        // Real-world regression sequence:
        // Update 1: RAW(index=0, id=ABC, name=RunCommand)
        // Update 2: RAW(index=0, id=null, args={...})
        // Update 3: CONTENT(id=ABC, name=RunCommand, complete args={...})
        var sequence = new List<ILLMOutput>
        {
            new ToolCallDelta("ABC", "RunCommand", "", 0),
            new ToolCallDelta("", null, "{\"commandLine\":\"Get-ChildItem -Path $PWD\",\"outputCharacterCount\":2000}", 0),
            new ToolCallDelta("ABC", "RunCommand", "{\"commandLine\":\"Get-ChildItem -Path $PWD\",\"outputCharacterCount\":2000}", null)
        };

        var (message, _, _) = await ToAsyncStream(sequence).AccumulateAsync();
        Assert.NotNull(message);
        var calls = message.Contents.OfType<ToolCall>().ToList();
        Assert.Single(calls);
        Assert.Equal("ABC", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
    }

    [Fact]
    public async Task AccumulateAsync_MultipleSimultaneousInterleavedCalls_ResolvesCorrectly()
    {
        // Two simultaneous tool calls where RAW chunks interleave:
        // index 0 + id A + name
        // index 1 + id B + name
        // index 0 + args fragment
        // index 1 + args fragment
        var sequence = new List<ILLMOutput>
        {
            new ToolCallDelta("A", "RunCommand", "", 0),
            new ToolCallDelta("B", "SearchWeb", "", 1),
            new ToolCallDelta("", null, "{\"commandLine\":", 0),
            new ToolCallDelta("", null, "{\"query\":", 1),
            new ToolCallDelta("", null, "\"ls\"}", 0),
            new ToolCallDelta("", null, "\"test\"}", 1)
        };

        var (message, _, _) = await ToAsyncStream(sequence).AccumulateAsync();
        Assert.NotNull(message);
        var calls = message.Contents.OfType<ToolCall>().ToList();
        
        Assert.Equal(2, calls.Count);
        
        Assert.Equal("A", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());

        Assert.Equal("B", calls[1].Id);
        Assert.Equal("SearchWeb", calls[1].Name);
        Assert.Contains("test", calls[1].Arguments.ToString());
    }

    [Fact]
    public async Task AccumulateAsync_AmbiguousDeltaWithMultipleActiveGroups_ThrowsInvalidOperationException()
    {
        var sequence = new List<ILLMOutput>
        {
            new ToolCallDelta("A", "RunCommand", "", 0),
            new ToolCallDelta("B", "SearchWeb", "", 1),
            new ToolCallDelta("", null, "{\"commandLine\":\"ls\"}", null) // Ambiguous chunk
        };

        await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
        {
            await ToAsyncStream(sequence).AccumulateAsync();
        });
    }
}
