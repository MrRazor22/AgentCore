using AgentCore.LLM;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Providers.Gemini;

public static class GeminiServiceExtensions
{
    public static AgentBuilder AddGemini(
        this AgentBuilder builder,
        Action<LLMOptions> configure,
        string? project = null,
        string? location = null)
    {
        builder.Services.Configure(configure);
        builder.Services.AddSingleton<ILLMProvider>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LLMOptions>>().Value;
            return new GeminiLLMClient(opts, project, location);
        });
        return builder;
    }
}
