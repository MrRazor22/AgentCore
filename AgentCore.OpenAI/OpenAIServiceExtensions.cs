using AgentCore.LLM;
using AgentCore.Runtime;
using AgentCore.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentCore.Providers.OpenAI;

public static class OpenAIServiceExtensions
{
    public static AgentBuilder AddOpenAI(this AgentBuilder builder, Action<LLMOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.Services.AddSingleton<ILLMProvider, OpenAILLMClient>();

        builder.Services.AddSingleton<ITokenCounter>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LLMOptions>>().Value;
            var encoding = opts.Model?.StartsWith("gpt-4o") == true ? "o200k_base" : "cl100k_base";
            return new TikTokenCounter(encoding);
        });

        return builder;
    }
}
