using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.MEAI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tests;

public class MEAITests
{
    private class MockChatClient : IChatClient
    {
        public List<IEnumerable<ChatMessage>> CapturedMessages { get; } = new();
        public List<ChatOptions?> CapturedOptions { get; } = new();

        private readonly List<ChatResponseUpdate> _streamingUpdates = new();

        public void AddStreamingUpdate(ChatResponseUpdate update)
        {
            _streamingUpdates.Add(update);
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedMessages.Add(messages.ToList());
            CapturedOptions.Add(options);

            foreach (var update in _streamingUpdates)
            {
                yield return update;
            }
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => this;

        public void Dispose() { }
    }

    [Fact]
    public async Task StreamAsync_MapsTextAndReasoning()
    {
        // Arrange
        var mockClient = new MockChatClient();
        mockClient.AddStreamingUpdate(new ChatResponseUpdate(ChatRole.Assistant, "Hello"));
        mockClient.AddStreamingUpdate(new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Thinking about reply...")]));
        mockClient.AddStreamingUpdate(new ChatResponseUpdate(ChatRole.Assistant, " World!"));

        var provider = new MEAILLM(mockClient);

        // Act
        var message = new StreamingMessage(provider.StreamAsync([new Message(Role.User, [new Text("Hi")])]));
        var contents = new List<IContent>();
        await foreach (var c in message.ContentsStream())
        {
            contents.Add(c);
        }

        // Assert
        var texts = contents.OfType<Text>().ToList();
        Assert.Equal(2, texts.Count);
        Assert.Equal("Hello", texts[0].Value);
        Assert.Equal(" World!", texts[1].Value);

        var reasoning = Assert.Single(contents.OfType<Reasoning>());
        Assert.Equal("Thinking about reply...", reasoning.Thought);
    }

    [Fact]
    public async Task StreamAsync_MapsUsageAndFinishReason()
    {
        // Arrange
        var mockClient = new MockChatClient();

        var usageDetails = new UsageDetails
        {
            InputTokenCount = 10,
            OutputTokenCount = 20,
            ReasoningTokenCount = 5
        };

        var contents = new List<AIContent> { new UsageContent(usageDetails) };
        mockClient.AddStreamingUpdate(new ChatResponseUpdate(ChatRole.Assistant, contents)
        {
            FinishReason = ChatFinishReason.Stop
        });

        var provider = new MEAILLM(mockClient);

        // Act
        var message = new StreamingMessage(provider.StreamAsync([new Message(Role.User, [new Text("Hi")])]));
        await foreach (var _ in message.ContentsStream()) { }

        // Assert
        Assert.Equal("stop", message.Metadata?.FinishReason);
        Assert.NotNull(message.Metadata?.Usage);
        Assert.Equal(10, message.Metadata.Usage.InputTokens);
        Assert.Equal(20, message.Metadata.Usage.OutputTokens);
    }

    [Fact]
    public async Task StreamAsync_AggregatesAndYieldsToolCalls()
    {
        // Arrange
        var mockClient = new MockChatClient();

        // Simulating incremental streaming chunks for a tool call
        mockClient.AddStreamingUpdate(new ChatResponseUpdate(ChatRole.Assistant, [
            new FunctionCallContent("call_1", "get_weather")
        ]));
        mockClient.AddStreamingUpdate(new ChatResponseUpdate(ChatRole.Assistant, [
            new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["location"] = "Seattle" })
        ]));

        var provider = new MEAILLM(mockClient);

        // Act
        var message = new StreamingMessage(provider.StreamAsync([new Message(Role.User, [new Text("What is the weather?")])]));
        var contents = new List<IContent>();
        await foreach (var c in message.ContentsStream())
        {
            contents.Add(c);
        }

        // Assert
        var toolCall = Assert.Single(contents.OfType<ToolCall>());
        Assert.Equal("call_1", toolCall.Id);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal("Seattle", toolCall.Arguments?["location"]?.ToString());
    }

    [Fact]
    public void WithMEAI_RegistersProvider()
    {
        // Arrange
        var mockClient = new MockChatClient();
        var builder = Agent.Create().WithMEAI(mockClient);

        // Act
        var agent = builder.Build();

        // Assert
        Assert.NotNull(agent);
        var llm = builder.GetService<ILLM>();
        Assert.NotNull(llm);
        if (llm is LLMLayer layer)
        {
            Assert.IsType<MEAILLM>(layer.Inner);
        }
        else
        {
            Assert.IsType<MEAILLM>(llm);
        }
    }

    private class TestAIFunction : AIFunction
    {
        public override string Name => "test_fn";
        public override string Description => "a description";
        public override JsonElement JsonSchema => JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"input\":{\"type\":\"string\"}}}").RootElement;

        public CancellationToken LastCancellationToken { get; private set; }
        public AIFunctionArguments? LastArguments { get; private set; }

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            LastArguments = arguments;
            return new ValueTask<object?>("result_value");
        }
    }

    [Fact]
    public async Task MEAIFunctionTool_PreservesFidelityAndPropagatesCancellation()
    {
        // Arrange
        var aiFunction = new TestAIFunction();
        var tool = new MEAIFunctionTool(aiFunction);

        // Act & Assert 1: Fidelity
        Assert.Equal("test_fn", tool.Definition.Name);
        Assert.Equal("a description", tool.Definition.Description);
        var schemaJson = tool.Definition.ParametersSchema.ToString();
        Assert.Contains("input", schemaJson);

        // Act & Assert 2: Execution and token propagation
        using var cts = new CancellationTokenSource();
        var arguments = new JsonObject { ["input"] = "hello" };
        var result = await tool.InvokeAsync(arguments, cts.Token);

        Assert.Equal("result_value", result);
        Assert.NotNull(aiFunction.LastArguments);
        Assert.Equal("hello", aiFunction.LastArguments["input"]?.ToString());
        Assert.Equal(cts.Token, aiFunction.LastCancellationToken);
    }

    [Fact]
    public async Task StreamAsync_PreservesModelAndResponseIdInMessageStart()
    {
        // Arrange
        var mockClient = new MockChatClient();
        mockClient.AddStreamingUpdate(new ChatResponseUpdate(ChatRole.Assistant, "Hello")
        {
            ModelId = "gpt-4o",
            ResponseId = "resp_123"
        });

        var provider = new MEAILLM(mockClient);

        // Act
        var message = new StreamingMessage(provider.StreamAsync([new Message(Role.User, [new Text("Hi")])]));
        await foreach (var _ in message.ContentsStream()) { }

        // Assert
        Assert.Equal("gpt-4o", message.Metadata?.Model);
        Assert.Equal("resp_123", message.Metadata?.Id);
    }
}
