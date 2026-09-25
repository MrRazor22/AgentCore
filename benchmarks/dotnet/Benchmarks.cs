using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using AgentCore;
using AgentCore.Context;
using AgentCore.Tool;
using AgentCore.Tool.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentCore.Benchmarks;

public class Program
{
    public static void Main(string[] args) => BenchmarkRunner.Run<AgentBenchmarks>();
}

[MemoryDiagnoser]
public class AgentBenchmarks
{
    private Agent _agentCore = null!;
    private ChatClientAgent _msAgent = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 1. AgentCore Setup
        var toolbox = new Toolbox([
            new MethodTool("get_weather", "Gets the weather", (string city) => $"15C and sunny in {city}")
        ]);
        _agentCore = new Agent(new MockLLM(), toolbox: toolbox, context: new ChatContext());

        // 2. Microsoft.Agents.AI Setup
        var weatherTool = AIFunctionFactory.Create((string city) => $"15C and sunny in {city}", "get_weather");
        var msClient = new MockMicrosoftChatClient();
        _msAgent = new ChatClientAgent(msClient, tools: [weatherTool]);
    }

    [Benchmark]
    public async Task<int> AgentCore_StreamingToolLoop()
    {
        int eventCount = 0;
        await foreach (var evt in _agentCore.InvokeStreamingAsync([new Text("What is the weather in London?")]))
        {
            eventCount++;
        }
        return eventCount;
    }

    [Benchmark]
    public async Task<int> MicrosoftAgents_StreamingToolLoop()
    {
        int chunkCount = 0;
        await foreach (var chunk in _msAgent.RunStreamingAsync([new ChatMessage(ChatRole.User, "What is the weather in London?")]))
        {
            chunkCount++;
        }
        return chunkCount;
    }
}
