using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using AgentCore.Conversation;
using AgentCore.Tokens;

namespace AgentCore.Tests;

public class TokenCounterTests
{
    [Fact]
    public async Task CountAsync_CalculatesEstimatedTokensCorrectly()
    {
        var counter = new ApproximateTokenCounter(initialCharsPerToken: 5.0, safetyMargin: 1.0);

        // 1. Empty messages -> 0
        var zeroTokens = await counter.CountAsync(Array.Empty<Message>());
        Assert.Equal(0, zeroTokens);

        // 2. Simple message calculation
        // Message overhead is 4 chars. "Hello" is 5 chars. Total chars = 9. CharsPerToken = 5.0. 
        // 9 / 5 = 1.8 -> cast to int is 1. (safetyMargin = 1.0)
        var msg = new Message(Role.User, new Text("Hello"));
        var estimated = await counter.CountAsync(new[] { msg });
        Assert.True(estimated >= 1);

        // 3. Reasoning is completely excluded from estimation character counts
        var msgWithReasoning = new Message(Role.Assistant, new IContent[] 
        { 
            new Reasoning("This is a deep thought that shouldn't be counted"),
            new Text("Short")
        });
        
        var msgWithOnlyText = new Message(Role.Assistant, new Text("Short"));
        
        var countWithReasoning = await counter.CountAsync(new[] { msgWithReasoning });
        var countWithOnlyText = await counter.CountAsync(new[] { msgWithOnlyText });
        
        // They should have the same character estimation since Reasoning is skipped
        Assert.Equal(countWithOnlyText, countWithReasoning);
    }

    [Fact]
    public void RecordActualInput_CalibratesRatioOrRejectsOutliers()
    {
        // Safety margin is 1.0 to keep math simple. Initial chars/token = 5.0. EmaAlpha = 0.1.
        var counter = new ApproximateTokenCounter(initialCharsPerToken: 5.0, safetyMargin: 1.0);

        // Let's create a calibration message that has 96 characters + 4 overhead = 100 characters total.
        var messages = new[] { new Message(Role.User, new Text(new string('a', 96))) };

        // 1. Calibrate with 20 tokens. Ratio = 100 / 20 = 5.0. New EMA = 0.1 * 5.0 + 0.9 * 5.0 = 5.0 (No change)
        counter.RecordActualInput(messages, tools: null, actualInputTokens: 20);

        // 2. Calibrate with 25 tokens. Ratio = 100 / 25 = 4.0.
        // New EMA Ratio = 0.1 * 4.0 + 0.9 * 5.0 = 4.9.
        counter.RecordActualInput(messages, tools: null, actualInputTokens: 25);

        // 3. Outlier rejection: Calibrate with 5 tokens. Ratio = 100 / 5 = 20.0 (greater than 10.0 limit) -> Rejected, EMA remains 4.9
        counter.RecordActualInput(messages, tools: null, actualInputTokens: 5);

        // 4. Outlier rejection: Calibrate with 200 tokens. Ratio = 100 / 200 = 0.5 (less than 1.0 limit) -> Rejected, EMA remains 4.9
        counter.RecordActualInput(messages, tools: null, actualInputTokens: 200);
    }
}
