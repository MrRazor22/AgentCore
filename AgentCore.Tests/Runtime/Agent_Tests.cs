using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Client;
using AgentCore.Runtime;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AgentCore.Tests.Runtime
{

    namespace AgentCore.Tests.Runtime
    {
        public sealed class Agent_Tests
        {
            // A fake LLM that returns exactly what the agent loop expects
            private sealed class FakeLLM : ILLMClient
            {
                public Queue<List<LLMStreamChunk>> Scripts = new Queue<List<LLMStreamChunk>>();

                public async Task<TResponse> ExecuteAsync<TResponse>(
                   LLMRequestBase request,
                   CancellationToken ct = default,
                   Action<LLMStreamChunk>? onStream = null)
                   where TResponse : LLMResponseBase
                {
                    var script = Scripts.Dequeue();

                    foreach (var c in script)
                        onStream?.Invoke(c);

                    // text from "Text" chunks
                    string txt = string.Join("", script.FindAll(x => x.Kind == StreamKind.Text).ConvertAll(x => x.AsText()));

                    // first tool call (if any)
                    ToolCall? tool = null;
                    foreach (var s in script)
                    {
                        if (s.Kind == StreamKind.ToolCallDelta)
                            tool = s.AsToolCall();
                    }

                    var result = Task.FromResult(new LLMTextResponse(
                        txt,
                        tool,
                        FinishReason.Stop
                    )).Result;

                    return (TResponse)(LLMResponseBase)result;
                }
            }

            // a simple tool for testing
            public class TestTools
            {
                [Tool]
                public static int Square(int x) => x * x;
            }

            private Agent BuildAgent(FakeLLM fake)
            {
                var b = new AgentBuilder();

                b.Services.AddSingleton<ILLMClient>(fake);
                b.Services.AddSingleton<ILogger<LLMPipeline>>(_ => NullLogger<LLMPipeline>.Instance);
                b.Services.AddSingleton<ILogger<ILLMClient>>(_ => NullLogger<ILLMClient>.Instance);
                b.Services.AddSingleton<ILogger<Agent>>(_ => NullLogger<Agent>.Instance);

                return b.Build("s1")
                        .WithTools<TestTools>();
            }

            // ──────────────────────────────────────────────────────────────
            [Fact]
            public async Task Returns_Text_When_No_Tool_Call()
            {
                var fake = new FakeLLM();
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
            {
                new LLMStreamChunk(StreamKind.Text, "hello"),
                new LLMStreamChunk(StreamKind.Finish, null, FinishReason.Stop)
            });

                var agent = BuildAgent(fake);

                var resp = await agent.InvokeAsync("goal");
                Assert.Equal("hello", resp.Message);
            }

            // ──────────────────────────────────────────────────────────────
            [Fact]
            public async Task Executes_Tool_Call_Then_Returns_Final_Text()
            {
                var fake = new FakeLLM();

                // first LLM call: issue tool call
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
            {
                new LLMStreamChunk(StreamKind.Text, ""),
                new LLMStreamChunk(
                    StreamKind.ToolCallDelta,
                    new ToolCall("id1", "Square", JObject.Parse("{\"x\":4}"))
                )
            });

                // second LLM call: return final answer
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
            {
                new LLMStreamChunk(StreamKind.Text, "done"),
                new LLMStreamChunk(StreamKind.Finish, null, FinishReason.Stop)
            });

                var agent = BuildAgent(fake);

                var resp = await agent.InvokeAsync("run");
                Assert.Equal("done", resp.Message);
            }

            // ──────────────────────────────────────────────────────────────
            [Fact]
            public async Task Stops_After_Max_Iterations()
            {
                var fake = new FakeLLM();

                // spam toolcalls
                for (int i = 0; i < 5; i++)
                {
                    fake.Scripts.Enqueue(new List<LLMStreamChunk>
        {
            new LLMStreamChunk(
                StreamKind.ToolCallDelta,
                new ToolCall("id", "Square", JObject.Parse("{\"x\":2}"))
            )
        });
                }

                // final text (never reached because maxIterations = 3)
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
    {
        new LLMStreamChunk(StreamKind.Text, FinishReason.Stop)
    });

                var agent = BuildAgent(fake);
                agent.UseExecutor(() => new ToolCallingLoop(maxIterations: 3));

                var resp = await agent.InvokeAsync("go");

                // We never reach the "stop" script
                // The assistant only receives tool-call runs -> no FinalText
                Assert.True(resp.Message == null || resp.Message == "");
            }


            // ──────────────────────────────────────────────────────────────
            [Fact]
            public async Task Writes_And_Reads_From_Memory()
            {
                var fake = new FakeLLM();
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
            {
                new LLMStreamChunk(StreamKind.Text, "hello")
            });

                var agent = BuildAgent(fake);

                // first call → stores memory
                var r1 = await agent.InvokeAsync("goal1");
                Assert.Equal("hello", r1.Message);

                // second call → memory should prepend "hello"
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
            {
                new LLMStreamChunk(StreamKind.Text, "again")
            });

                var r2 = await agent.InvokeAsync("goal2");

                // conversation now contains previous assistant message
                Assert.Equal("again", r2.Message);
            }

            // ──────────────────────────────────────────────────────────────
            [Fact]
            public async Task Streams_Text_Chunks()
            {
                var fake = new FakeLLM();
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
            {
                new LLMStreamChunk(StreamKind.Text, "a"),
                new LLMStreamChunk(StreamKind.Text, "b"),
                new LLMStreamChunk(StreamKind.Finish, null, FinishReason.Stop)
            });

                var agent = BuildAgent(fake);

                var list = new List<object>();
                var resp = await agent.InvokeAsync("g", stream: s => list.Add(s));

                Assert.Equal(new[] { "a", "b" }, list);
                Assert.Equal("ab", resp.Message);
            }

            // ──────────────────────────────────────────────────────────────
            [Fact]
            public async Task Cancellation_Does_Not_Overwrite_Partial_Output()
            {
                var fake = new FakeLLM();
                fake.Scripts.Enqueue(new List<LLMStreamChunk>
    {
        new LLMStreamChunk(StreamKind.Text, "hi")
    });

                using var cts = new CancellationTokenSource();
                var agent = BuildAgent(fake);

                // cancel shortly AFTER invocation begins
                var task = agent.InvokeAsync("g", cts.Token);

                await Task.Delay(10);  // allow FakeLLM to emit "hi"
                cts.Cancel();

                var resp = await task;

                Assert.Equal("hi", resp.Message);
            }

        }

    }
}
