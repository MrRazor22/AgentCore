using AgentCore.LLM;
using AgentCore.Tokens;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace AgentCore.Providers.MEAI;

public static class MEAIServiceExtensions
{
    /// <summary>
    /// Adds a Microsoft.Extensions.AI IChatClient as the LLM provider for the agent.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="client">The IChatClient instance to wrap.</param>
    /// <param name="options">Optional LLM options.</param>
    /// <returns>The builder instance.</returns>
    public static AgentBuilder AddMEAI(this AgentBuilder builder, IChatClient client, LLMOptions? options = null)
    {
        var provider = new MEAILLMClient(client);
        builder.WithProvider(provider, options);
        builder.WithTokenCounter(new ApproximateTokenCounter());
        return builder;
    }

    /// <summary>
    /// Adds an OpenAI-compatible provider (OpenAI, LM Studio, etc.) to the agent.
    /// </summary>
    public static AgentBuilder AddOpenAI(this AgentBuilder builder, string model, string? apiKey = null, string? baseUrl = null, LLMOptions? options = null)
    {
        var openAiOptions = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(baseUrl))
            openAiOptions.Endpoint = new Uri(baseUrl);
        
        var credentials = new ApiKeyCredential(apiKey ?? throw new ArgumentNullException(nameof(apiKey)));
        var openAiClient = new OpenAIClient(credentials, openAiOptions);
        var chatClient = openAiClient.GetChatClient(model).AsIChatClient();
        
        return builder.AddMEAI(chatClient, options);
    }

    /// <summary>
    /// Adds an Anthropic Claude provider to the agent.
    /// </summary>
    public static AgentBuilder AddAnthropic(this AgentBuilder builder, string model, string apiKey, LLMOptions? options = null)
    {
        var anthropicClient = new AnthropicClient { ApiKey = apiKey };
        var chatClient = anthropicClient.AsIChatClient(model);
        
        return builder.AddMEAI(chatClient, options);
    }

    /// <summary>
    /// Adds an Ollama provider to the agent.
    /// </summary>
    public static AgentBuilder AddOllama(this AgentBuilder builder, string model, string? baseUrl = null, LLMOptions? options = null)
    {
        var uri = string.IsNullOrEmpty(baseUrl) ? new Uri("http://localhost:11434") : new Uri(baseUrl);
        var chatClient = new OllamaChatClient(uri, model);
        
        return builder.AddMEAI(chatClient, options);
    }
}
