using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using AgentCore.Conversation;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tests;

public class TokenTests
{
    [Fact]
    public async Task ApproximateTokenCounter_CountsAndCalibratesCorrectly()
    {
        // Arrange
        var counter = new ApproximateTokenCounter(NullLogger<ApproximateTokenCounter>.Instance);
        var messages = new List<Message>
        {
            new Message(Role.User, new Text("Hello, testing tokens."))
        };

        // Act - Estimation
        var initialEstimate = await counter.CountAsync(messages);
        
        // Assert - Basic estimation validation
        Assert.True(initialEstimate > 0);

        // Act - Calibrate to a new ratio
        // Initial cpt = 5.0, let's calibrate with actualTokenCount that represents cpt = 2.0
        // CharCount of "Hello, testing tokens." with overhead is 23 (text length) + 4 (overhead) = 27 chars.
        // Let's calibrate actualTokenCount = 13 tokens. cpt ratio becomes 27/13 = 2.07
        counter.Calibrate(messages, 13);
        
        var calibratedEstimate = await counter.CountAsync(messages);

        // Assert - Padded estimate should increase because charsPerToken decreased (estimated tokens = charCount / cpt)
        Assert.True(calibratedEstimate > initialEstimate);
    }

    [Fact]
    public void TokenManager_AccumulatesCumulativeUsage()
    {
        // Arrange
        var manager = new TokenManager(NullLogger<TokenManager>.Instance);

        // Act
        manager.Record(new TokenUsage(100, 50, 10));
        manager.Record(new TokenUsage(200, 100, 20));

        // Assert
        var totals = manager.GetTotals();
        Assert.Equal(300, totals.InputTokens);
        Assert.Equal(150, totals.OutputTokens);
        Assert.Equal(30, totals.ReasoningTokens);
        Assert.Equal(480, totals.Total);
    }
}
