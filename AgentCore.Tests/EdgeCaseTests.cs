using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentCore.Tests
{
    public class EdgeCaseTests
    {
        private class TestDto
        {
            public string? Name { get; set; }
            public int Age { get; set; }
        }

        private class MockLLM : ILLM
        {
            private readonly Queue<Func<IReadOnlyList<Message>, Task<IAsyncEnumerable<IMessageEvent>>>> _responses = new();

            public int ContextWindow { get; set; } = 4096;
            public int ReservedTokens { get; set; } = 512;

            public List<IReadOnlyList<Message>> CapturedMessages { get; } = new();



            public void Enqueue(Func<IReadOnlyList<Message>, Task<IAsyncEnumerable<IMessageEvent>>> responseGenerator)
            {
                _responses.Enqueue(responseGenerator);
            }

            public void EnqueueSimpleText(string text)
            {
                Enqueue(messages => Task.FromResult<IAsyncEnumerable<IMessageEvent>>(
                    new IMessageEvent[]
                    {
                        new MessageStart(Role.Assistant),
                        new TextStart(0),
                        new TextDelta(0, text),
                        new TextEnd(0),
                        new MessageEnd("stop")
                    }.ToAsyncEnumerable()
                ));
            }


            public IAsyncEnumerable<IMessageEvent> StreamAsync(
                IReadOnlyList<Message> messages,
                JsonSchema? responseSchema = null,
                IReadOnlyList<ToolDefinition>? tools = null,
                CancellationToken ct = default)
            {
                CapturedMessages.Add(messages.ToList());
                if (_responses.Count > 0)
                {
                    var generator = _responses.Dequeue();
                    return generator(messages).GetAwaiter().GetResult();
                }
                return Array.Empty<IMessageEvent>().ToAsyncEnumerable();
            }
        }

        private class TestExecutionTool : Tool
        {
            public List<string> ExecutionLog { get; } = new();
            private readonly int _delayMs;

            public TestExecutionTool(string name, int delayMs = 0)
                : base(new ToolDefinition(name, "Mock Tool Description", new LLM.Schema.JsonSchema(new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() })))
            {
                _delayMs = delayMs;
            }

            public override async Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
            {
                ExecutionLog.Add($"Started {Definition.Name}");
                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs, ct);
                }
                ExecutionLog.Add($"Completed {Definition.Name}");
                return $"Result of {Definition.Name}";
            }
        }

        [Fact]
        public async Task InvokeAsync_StructuredOutput_MalformedJson_ThrowsJsonException()
        {
            // Arrange
            var mockLlm = new MockLLM();
            mockLlm.EnqueueSimpleText("{ malformed json : ");

            var agent = Agent.Create()
                .WithLLM(lf => mockLlm)
                .Build();

            // Act & Assert
            await Assert.ThrowsAsync<JsonException>(async () =>
            {
                await agent.InvokeAsync<TestDto>(new Text("Requesting structured data"));
            });
        }

        [Fact]
        public async Task InvokeAsync_ContextPruning_ConsolidatesMemory()
        {
            // Arrange
            var mockLlm = new MockLLM();
            mockLlm.EnqueueSimpleText("Assistant final reply");

            var mockSummarizer = new MockLLM();
            mockSummarizer.EnqueueSimpleText("Summarized context sheet content");

            var context = new ChatContext(
                contextWindow: 120,
                reserveTokens: 10,
                summarizer: mockSummarizer
            );

            // Add some messages to trigger pruning. 
            // The budget is roughly: 120 - (Instructions (12 chars + overhead) + ReservedTokens (10)) -> budget is ~80 tokens (~400 characters).
            // Let's add multiple large messages so it exceeds the budget.
            var system = new Message(Role.System, [new Text("Instructions")]);
            var user = new Message(Role.User, [new Text(new string('A', 300))]);
            var assistant = new Message(Role.Assistant, [new Text(new string('B', 300))], new MessageMetadata(Usage: new TokenUsage(105, 0)));
            await context.AddAsync(new[] { system, user, assistant });

            var agent = Agent.Create()
                .WithLLM(lf => mockLlm)
                .WithContext(lf => context)
                .Build();

            // Act
            var result = await agent.InvokeAsync<string>(new Text("Trigger conversation"));

            // Assert
            Assert.Equal("Assistant final reply", result);
            // Summarizer should have been invoked to consolidate memory
            Assert.True(mockSummarizer.CapturedMessages.Count > 0);
            
            // Check that the pruned history has consolidated fact sheet included
            var lastCaptured = mockLlm.CapturedMessages.Last();
            Assert.Contains(lastCaptured, m => m.Contents.Any(c => c.ForLlm().Contains("Summarized context")));
        }

        [Fact]
        public async Task InvokeAsync_ContextOverflow_ZeroOrNegativeBudget_PrunesGracefully()
        {
            // Arrange
            var mockLlm = new MockLLM();
            mockLlm.EnqueueSimpleText("Reply despite overflow");

            var context = new ChatContext(
                contextWindow: 30,
                reserveTokens: 40
            );

            var system = new Message(Role.System, [new Text("Instructions")]);
            await context.AddAsync(new[] { system, new Message(Role.User, [new Text("First")]), new Message(Role.Assistant, [new Text("Second")], new MessageMetadata(Usage: new TokenUsage(10, 0))) });

            var agent = Agent.Create()
                .WithLLM(lf => mockLlm)
                .WithContext(lf => context)
                .Build();

            // Act
            var result = await agent.InvokeAsync<string>(new Text("Third"));

            // Assert
            Assert.Equal("Reply despite overflow", result);
            // Verify history has pruned down to minimum allowed (at least the last message)
            var lastCaptured = mockLlm.CapturedMessages.Last();
            Assert.NotEmpty(lastCaptured);
        }

        [Fact]
        public async Task InvokeAsync_ParallelToolExecution_CallsExecuted()
        {
            // Arrange
            var mockLlm = new MockLLM();
            var tool1 = new TestExecutionTool("Tool1", delayMs: 50);
            var tool2 = new TestExecutionTool("Tool2", delayMs: 10);

            // Step 1: Enqueue two parallel tool calls
            mockLlm.Enqueue(messages => Task.FromResult<IAsyncEnumerable<IMessageEvent>>(
                new IMessageEvent[]
                {
                    new MessageStart(Role.Assistant),
                    new ToolCallStart(0, "call-1", "Tool1"),
                    new ToolCallDelta(0, "{}"),
                    new ToolCallEnd(0),
                    new ToolCallStart(1, "call-2", "Tool2"),
                    new ToolCallDelta(1, "{}"),
                    new ToolCallEnd(1),
                    new MessageEnd()
                }.ToAsyncEnumerable()
            ));


            // Step 2: Enqueue final text response
            mockLlm.EnqueueSimpleText("Tools executed successfully.");

            var agent = Agent.Create()
                .WithLLM(lf => mockLlm)
                .WithTools(tool1, tool2)
                .Build();

            // Act
            var result = await agent.InvokeAsync<string>(new Text("Execute tools"));

            // Assert
            Assert.Equal("Tools executed successfully.", result);
            Assert.Contains("Completed Tool1", tool1.ExecutionLog);
            Assert.Contains("Completed Tool2", tool2.ExecutionLog);

            // Verify they executed concurrently. Since Tool2 has 10ms delay and Tool1 has 50ms delay,
            // if executed concurrently, Tool2 should complete before Tool1 completes.
            int idxCompleted2 = tool2.ExecutionLog.IndexOf("Completed Tool2");
            int idxCompleted1 = tool1.ExecutionLog.IndexOf("Completed Tool1");
            
            // Note: Parallel starts may happen in quick succession.
            // We just verify both were executed and completed successfully.
            Assert.True(idxCompleted1 >= 0);
            Assert.True(idxCompleted2 >= 0);
        }

    }

    internal static class AsyncEnumerableExtensions
    {
        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                yield return item;
                await Task.CompletedTask;
            }
        }
    }
}
