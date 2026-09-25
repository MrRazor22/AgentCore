using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace AgentCore.Benchmarks;

public sealed class MockMicrosoftChatClient : IChatClient
{
    private int _invocationCount;

    public ChatClientMetadata Metadata { get; } = new("MockProvider");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messagesList = chatMessages.ToList();
        var lastMessage = messagesList.LastOrDefault();

        if (_invocationCount++ % 2 == 0)
        {
            var callContent = new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "London" });
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [callContent])]));
        }

        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "The weather in London is 15C and sunny.")]));
    }

    public async IAsyncEnumerable<StreamingChatCompletionUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var messagesList = chatMessages.ToList();

        if (_invocationCount++ % 2 == 0)
        {
            yield return new StreamingChatCompletionUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "London" })]
            };
        }
        else
        {
            yield return new StreamingChatCompletionUpdate
            {
                Role = ChatRole.Assistant,
                Text = "The weather in London is 15C and sunny."
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(ChatClientMetadata) ? Metadata : null;

    public void Dispose() { }
}
