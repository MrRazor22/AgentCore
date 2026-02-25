using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Runtime;

public sealed class AgentConfig
{
    public string Name { get; set; } = "agent";
    public string? SystemPrompt { get; set; }
    public int MaxIterations { get; set; } = 50;
    public Type? OutputType { get; set; }
}

public sealed class AgentBuilder
{
    private readonly AgentConfig _config = new();
    private readonly List<Action<ToolRegistryCatalog>> _toolRegistrations = [];
    public IServiceCollection Services { get; } = new ServiceCollection();

    public AgentBuilder()
    {
        Services.AddLogging();
        Services.AddSingleton<IAgentMemory, FileMemory>();
        Services.AddSingleton<IContextManager, ContextManager>();
        Services.AddSingleton<ITokenManager, TokenManager>();
        Services.AddSingleton<ITokenCounter, ApproximateTokenCounter>();
        Services.AddScoped<IToolExecutor, ToolExecutor>();
        Services.AddScoped<IToolCallParser, ToolCallParser>();
        Services.AddScoped<ILLMExecutor, LLMExecutor>();
        Services.AddTransient(typeof(IAgentExecutor), typeof(ToolCallingLoop));
    }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithInstructions(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }
    public AgentBuilder WithOutput<T>() { _config.OutputType = typeof(T); return this; }
    public AgentBuilder ConfigureServices(Action<IServiceCollection> configure) { configure(Services); return this; }

    public LLMAgent Build()
    {
        Services.AddScoped<ToolRegistryCatalog>(sp =>
        {
            var reg = new ToolRegistryCatalog();
            foreach (var init in _toolRegistrations) init(reg);
            return reg;
        });

        Services.AddScoped<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
        Services.AddScoped<IToolCatalog>(sp => sp.GetRequiredService<ToolRegistryCatalog>());

        var provider = Services.BuildServiceProvider(validateScopes: true);

        if (provider.GetService<ILLMProvider>() == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call AddOpenAI().");

        return new LLMAgent(provider, _config);
    }
}
