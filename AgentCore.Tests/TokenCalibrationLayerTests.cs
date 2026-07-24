using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Tests;

public class TokenCalibrationLayerTests
{
    private class MockTokenCounter : ITokenCounter
    {
        public List<(IEnumerable<Message> Messages, IReadOnlyList<Tool>? Tools, int ActualInputTokens)> Observations { get; } = new();

        public Task<int> EstimateAsync(IEnumerable<Message> messages, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> EstimateAsync(IEnumerable<Tool> tools, CancellationToken ct = default) => Task.FromResult(0);

        public void ObserveActualCount(IEnumerable<Message> messages, IReadOnlyList<Tool>? tools, int actualInputTokens)
        {
            Observations.Add((messages, tools, actualInputTokens));
        }
    }

    [Fact]
    public async Task StreamAsync_InterceptsMetadataAndTriggersCalibration()
    {
        // Arrange
        var tokenCounter = new MockTokenCounter();
        var mockLLM = new MockLLMProvider();

        // Enqueue some outputs including a Metadata output
        mockLLM.Enqueue(
            new Text("Hello"),
            new Metadata(InputTokens: 42, OutputTokens: 10)
        );

        var calibrationLayer = new TokenCalibrationLayer(tokenCounter);
        calibrationLayer.Attach(mockLLM);

        var messages = new[] { new Message(Role.User, new Text("Hi")) };

        // Act
        var stream = calibrationLayer.StreamAsync(messages);
        await foreach (var item in stream)
        {
            // Consume the stream
        }

        // Assert
        Assert.Single(tokenCounter.Observations);
        var obs = tokenCounter.Observations[0];
        Assert.Equal(42, obs.ActualInputTokens);
        Assert.Equal(messages, obs.Messages);
    }
}
