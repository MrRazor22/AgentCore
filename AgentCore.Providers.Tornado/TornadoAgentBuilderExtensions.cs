namespace AgentCore.Providers.Tornado;

public static class TornadoAgentBuilderExtensions
{
    public static AgentBuilder AddTornado(this AgentBuilder builder, string apiKey, string modelName, Uri? baseUrl = null)
        => builder.WithProvider(TornadoProvider.CreateLLMProvider(apiKey, modelName, baseUrl));

}
