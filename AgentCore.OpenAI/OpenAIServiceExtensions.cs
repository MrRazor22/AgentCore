using AgentCore.LLM;
using AgentCore.Tokens;


namespace AgentCore.Providers.OpenAI;

public static class OpenAIServiceExtensions
{
    public static AgentBuilder AddOpenAI(this AgentBuilder builder, Action<LLMOptions> configure)
    {
        var options = new LLMOptions();
        configure(options);

        var provider = new OpenAILLMClient(options);
        builder.WithProvider(provider);

        var encoding = options.Model?.StartsWith("gpt-4o") == true ? "o200k_base" : "cl100k_base";
        var tokenCounter = new TikTokenCounter(encoding);
        builder.WithTokenCounter(tokenCounter);

        return builder;
    }
}
