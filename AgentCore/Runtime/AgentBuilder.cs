using AgentCore.Chat;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly List<Action<ToolRegistry>> _toolRegistrations = [];
    public IServiceCollection Services { get; } = new ServiceCollection();

    // Callback storage
    private Func<ToolCall, CancellationToken, Task<IContent?>>? _beforeToolCall;
    private Func<ToolCall, IContent?, CancellationToken, Task<IContent?>>? _afterToolCall;
    private Func<IReadOnlyList<Message>, LLMOptions, CancellationToken, Task<IReadOnlyList<LLMEvent>?>>? _beforeModelCall;
    private Func<IReadOnlyList<LLMEvent>, CancellationToken, Task>? _afterModelCall;

    public AgentBuilder()
    {
        Services.AddLogging();
        Services.AddSingleton<IAgentMemory, FileMemory>();
        Services.AddSingleton<IContextManager, ContextManager>();
        Services.AddSingleton<ITokenCounter, ApproximateTokenCounter>();
        Services.AddSingleton<ITokenManager, TokenManager>();
        Services.AddScoped<IToolCallParser, ToolCallParser>();
        Services.AddTransient<IAgentExecutor>(sp => new ToolCallingLoop(
            sp.GetRequiredService<IAgentMemory>(),
            sp.GetRequiredService<ILogger<ToolCallingLoop>>()
        ));
    }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithInstructions(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }
    public AgentBuilder WithOutput<T>() { _config.OutputType = typeof(T); return this; }
    public AgentBuilder ConfigureServices(Action<IServiceCollection> configure) { configure(Services); return this; }

    // Executor callbacks
    public AgentBuilder BeforeToolCall(Func<ToolCall, CancellationToken, Task<IContent?>> callback) { _beforeToolCall = callback; return this; }
    public AgentBuilder AfterToolCall(Func<ToolCall, IContent?, CancellationToken, Task<IContent?>> callback) { _afterToolCall = callback; return this; }
    public AgentBuilder BeforeModelCall(Func<IReadOnlyList<Message>, LLMOptions, CancellationToken, Task<IReadOnlyList<LLMEvent>?>> callback) { _beforeModelCall = callback; return this; }
    public AgentBuilder AfterModelCall(Func<IReadOnlyList<LLMEvent>, CancellationToken, Task> callback) { _afterModelCall = callback; return this; }

    public LLMAgent Build()
    {
        Services.AddScoped<ToolRegistry>(sp =>
        {
            var reg = new ToolRegistry();
            foreach (var init in _toolRegistrations) init(reg);
            return reg;
        });

        Services.AddScoped<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistry>());

        // Wire executor callbacks via DI factories
        Services.AddScoped<IToolExecutor>(sp => new ToolExecutor(sp.GetRequiredService<IToolRegistry>())
        {
            BeforeCall = _beforeToolCall,
            AfterCall = _afterToolCall
        });

        Services.AddScoped<ILLMExecutor>(sp => new LLMExecutor(
            sp.GetRequiredService<ILLMProvider>(),
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<IContextManager>(),
            sp.GetRequiredService<ITokenManager>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LLMExecutor>>())
        {
            BeforeCall = _beforeModelCall,
            AfterCall = _afterModelCall
        });

        var provider = Services.BuildServiceProvider(validateScopes: true);

        if (provider.GetService<ILLMProvider>() == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call AddOpenAI().");

        return new LLMAgent(provider, _config);
    }
}
