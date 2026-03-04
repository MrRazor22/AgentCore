using AgentCore.LLM;

namespace AgentCore.Providers.Gemini;

public static class GeminiServiceExtensions
{
    public static AgentBuilder AddGemini(
        this AgentBuilder builder,
        Action<LLMOptions> configure,
        string? project = null,
        string? location = null)
    {
        var options = new LLMOptions();
        configure(options);

        var provider = new GeminiLLMClient(options, project, location);
        builder.WithProvider(provider);

        return builder;
    }
}
