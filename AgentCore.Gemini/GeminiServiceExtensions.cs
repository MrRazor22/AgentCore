using AgentCore.Providers;
using AgentCore.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Providers.Gemini;

public sealed class GeminiInitOptions : LLMInitOptions
{
    public string? Project { get; set; }
    public string? Location { get; set; }
}

public static class GeminiServiceExtensions
{
    public static AgentBuilder AddGemini(this AgentBuilder builder, Action<GeminiInitOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.Services.AddSingleton<ILLMStreamProvider, GeminiLLMClient>();
        return builder;
    }
}
