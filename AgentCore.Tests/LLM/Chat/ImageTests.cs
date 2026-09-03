using System;
using System.Text.Json;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests.ChatTests;

public class ImageTests
{
    [Fact]
    public void EstimateTokens_WithoutDimensions_ReturnsDefault1000()
    {
        var image = new Image(Uri: new Uri("https://example.com/test.png"));
        Assert.Equal(1000, image.EstimateTokens());
    }

    [Fact]
    public void EstimateTokens_TinyDimensions_EnforcesMinimum85Tokens()
    {
        var image = new Image(Width: 10, Height: 10);
        Assert.Equal(85, image.EstimateTokens());
    }

    [Fact]
    public void EstimateTokens_StandardDimensions_CalculatesPixelHeuristic()
    {
        // 1000 x 750 = 750,000 pixels / 750 = 1000 tokens
        var image = new Image(Width: 1000, Height: 750);
        Assert.Equal(1000, image.EstimateTokens());
    }

    [Fact]
    public void EstimateTokens_HighResDimensions_ScalesProportionally()
    {
        // 3000 x 2000 = 6,000,000 pixels / 750 = 8000 tokens
        var image = new Image(Width: 3000, Height: 2000);
        Assert.Equal(8000, image.EstimateTokens());
    }

    [Fact]
    public void Truncate_WithinBudget_ReturnsSameInstance()
    {
        var image = new Image(Width: 500, Height: 500); // 250,000 / 750 = 334 tokens
        var truncated = image.Truncate(500);
        Assert.Same(image, truncated);
    }

    [Fact]
    public void Truncate_ExceedsBudget_ReturnsTextPlaceholderWithoutDataCorruption()
    {
        var image = new Image(Width: 3000, Height: 2000, MediaType: "image/jpeg");
        var truncated = image.Truncate(500);

        var textContent = Assert.IsType<Text>(truncated);
        Assert.Contains("[Image (image/jpeg) omitted: exceeds context budget]", textContent.Value);
    }

    [Fact]
    public void PolymorphicSerialization_RoundtripsSuccessfully()
    {
        IContent original = new Image(
            Uri: new Uri("https://example.com/cat.jpg"),
            MediaType: "image/jpeg",
            Width: 1024,
            Height: 768);

        string json = JsonSerializer.Serialize(original);
        Assert.Contains("\"type\":\"image\"", json);
        Assert.Contains("\"media_type\":\"image/jpeg\"", json);

        var deserialized = JsonSerializer.Deserialize<IContent>(json);
        var image = Assert.IsType<Image>(deserialized);
        Assert.Equal(1024, image.Width);
        Assert.Equal(768, image.Height);
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal(new Uri("https://example.com/cat.jpg"), image.Uri);
    }
}
