using System;
using AgentCore.Context.Primitives;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests.ChatTests;

public class ImageTests
{
    private readonly Tokenizer _estimator = new();
    private readonly Truncator _truncator;

    public ImageTests()
    {
        _truncator = new Truncator(_estimator);
    }

    [Fact]
    public void EstimateTokens_WithoutDimensions_ReturnsDefault1000()
    {
        var image = new Image(Uri: new Uri("https://example.com/test.png"));
        Assert.Equal(1000, _estimator.Estimate(image));
    }

    [Fact]
    public void EstimateTokens_TinyDimensions_EnforcesMinimum85Tokens()
    {
        var image = new Image(Width: 10, Height: 10);
        Assert.Equal(85, _estimator.Estimate(image));
    }

    [Fact]
    public void EstimateTokens_StandardDimensions_CalculatesPixelHeuristic()
    {
        // 1000 x 750 = 750,000 pixels / 750 = 1000 tokens
        var image = new Image(Width: 1000, Height: 750);
        Assert.Equal(1000, _estimator.Estimate(image));
    }

    [Fact]
    public void EstimateTokens_HighResDimensions_ScalesProportionally()
    {
        // 3000 x 2000 = 6,000,000 pixels / 750 = 8000 tokens
        var image = new Image(Width: 3000, Height: 2000);
        Assert.Equal(8000, _estimator.Estimate(image));
    }

    [Fact]
    public void Truncate_WithinBudget_ReturnsSameInstance()
    {
        var image = new Image(Width: 500, Height: 500); // 250,000 / 750 = 334 tokens
        var truncated = _truncator.Truncate(image, 500);
        Assert.Same(image, truncated);
    }

    [Fact]
    public void Truncate_ExceedsBudget_ReturnsTextPlaceholderWithoutDataCorruption()
    {
        var image = new Image(Width: 3000, Height: 2000, MediaType: "image/jpeg");
        var truncated = _truncator.Truncate(image, 500);

        var textContent = Assert.IsType<Text>(truncated);
        Assert.Contains("[Image (image/jpeg) omitted: exceeds context budget]", textContent.Value);
    }
}
