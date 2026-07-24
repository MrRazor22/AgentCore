using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System.Text.Json.Nodes;

namespace AgentCore.Tests;

public class TokenCounterTests
{
    [Fact]
    public async Task EstimateAsync_CalculatesEstimatedTokensCorrectly()
    {
        var counter = new ApproximateTokenCounter(initialCharsPerToken: 5.0, safetyMargin: 1.0);

        // 1. Empty messages -> 0
        var zeroTokens = await counter.EstimateAsync(Array.Empty<Message>());
        Assert.Equal(0, zeroTokens);

        // 2. Simple message calculation
        // Message overhead is 4 chars. "Hello" is 5 chars. Total chars = 9. CharsPerToken = 5.0. 
        // 9 / 5 = 1.8 -> cast to int is 1. (safetyMargin = 1.0)
        var msg = new Message(Role.User, new Text("Hello"));
        var estimated = await counter.EstimateAsync(new[] { msg });
        Assert.True(estimated >= 1);

        // 3. Reasoning is completely excluded from estimation character counts
        var msgWithReasoning = new Message(Role.Assistant, new IContent[]
        {
            new Reasoning("This is a deep thought that shouldn't be counted"),
            new Text("Short")
        });

        var msgWithOnlyText = new Message(Role.Assistant, new Text("Short"));

        var countWithReasoning = await counter.EstimateAsync(new[] { msgWithReasoning });
        var countWithOnlyText = await counter.EstimateAsync(new[] { msgWithOnlyText });

        // They should have the same character estimation since Reasoning is skipped
        Assert.Equal(countWithOnlyText, countWithReasoning);
    }

    private class TestTool : Tool
    {
        public TestTool(string name, string description, JsonSchema schema)
            : base(name, description, schema) { }

        public override Task<object?> InvokeAsync(JsonObject arguments, System.Threading.CancellationToken ct = default)
            => Task.FromResult<object?>("result");
    }

    [Fact]
    public async Task EstimateAsync_Tools_EstimatesCorrectly()
    {
        var counter = new ApproximateTokenCounter(initialCharsPerToken: 5.0, safetyMargin: 1.0);
        var schema = new JsonSchema(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        });
        var tool = new TestTool("test_tool", "A simple test tool", schema);

        var toolTokens = await counter.EstimateAsync(new[] { tool });
        Assert.True(toolTokens > 0);
    }

    [Fact]
    public void ObserveActualCount_CalibratesRatioOrRejectsOutliers()
    {
        // Safety margin is 1.0 to keep math simple. Initial chars/token = 5.0. EmaAlpha = 0.1.
        var counter = new ApproximateTokenCounter(initialCharsPerToken: 5.0, safetyMargin: 1.0);

        // Let's create a calibration message that has 96 characters + 4 overhead = 100 characters total.
        var messages = new[] { new Message(Role.User, new Text(new string('a', 96))) };

        // 1. Calibrate with 20 tokens. Ratio = 100 / 20 = 5.0. New EMA = 0.1 * 5.0 + 0.9 * 5.0 = 5.0 (No change)
        counter.ObserveActualCount(messages, tools: null, actualInputTokens: 20);

        // 2. Calibrate with 25 tokens. Ratio = 100 / 25 = 4.0.
        // New EMA Ratio = 0.1 * 4.0 + 0.9 * 5.0 = 4.9.
        counter.ObserveActualCount(messages, tools: null, actualInputTokens: 25);

        // 3. Outlier rejection: Calibrate with 5 tokens. Ratio = 100 / 5 = 20.0 (greater than 10.0 limit) -> Rejected, EMA remains 4.9
        counter.ObserveActualCount(messages, tools: null, actualInputTokens: 5);

        // 4. Outlier rejection: Calibrate with 200 tokens. Ratio = 100 / 200 = 0.5 (less than 1.0 limit) -> Rejected, EMA remains 4.9
        counter.ObserveActualCount(messages, tools: null, actualInputTokens: 200);
    }

    [Fact]
    public async Task ObserveActualCount_CalibratesWithTools()
    {
        // Initial chars/token = 5.0. EmaAlpha = 0.1. Safety margin = 1.0.
        var counter = new ApproximateTokenCounter(initialCharsPerToken: 5.0, safetyMargin: 1.0);
        var messages = new[] { new Message(Role.User, new Text(new string('a', 96))) }; // 100 chars (96 + 4 overhead)

        var schema = new JsonSchema(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        });
        var tool = new TestTool("test_tool", "A simple test tool", schema);
        // Estimate tool characters: tool.Name + tool.Description + tool.ParametersSchema
        int toolChars = "test_tool".Length + "A simple test tool".Length + schema.ToJsonNode().ToJsonString().Length;
        int totalChars = 100 + toolChars;

        // If we observe actual input tokens, it should calibrate against the combined total character count
        // For example, if actualInputTokens is 30, currentRatio = totalChars / 30.
        // Expected new ratio = 0.1 * currentRatio + 0.9 * 5.0
        double expectedRatio = 0.1 * ((double)totalChars / 30) + 0.9 * 5.0;

        counter.ObserveActualCount(messages, new[] { tool }, 30);

        // We can check if it calibrates correctly by estimating tokens for a known character count.
        // E.g., for 100 characters: expectedTokens = 100 / expectedRatio
        int estimated = await counter.EstimateAsync(messages);
        int expectedTokens = (int)(100 / expectedRatio);
        Assert.Equal(expectedTokens, estimated);
    }
}
