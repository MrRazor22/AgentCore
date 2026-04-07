using AgentCore.Conversation;
using AgentCore.Execution;
using AgentCore.LLM;
using AgentCore.Runtime;
using AgentCore.Tokens;
using AgentCore.Tooling;
using AgentCore.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public sealed class AgentConfig
{
    public string Name { get; set; } = "agent";
    public string? SystemPrompt { get; set; }
    public int? MaxToolCalls { get; set; } = null;
    public ToolOptions ToolOptions { get; set; } = new();
}

public sealed class AgentBuilder
{
    private readonly AgentConfig _config = new();
    private readonly List<Action<ToolRegistry>> _toolRegistrations = [];

    public IAgentMemory? Memory { get; set; }
    public IContextCompactor? ContextCompactor { get; set; }
    public IContextAssembler? ContextAssembler { get; set; }
    public IMemory? KnowledgeMemory { get; set; }
    public ITokenCounter? TokenCounter { get; set; }
    public ITokenManager? TokenManager { get; set; }
    public ILoggerFactory? LoggerFactory { get; set; }
    public ILLMProvider? Provider { get; set; }
    private LLMOptions? _providerOptions;

    private readonly List<ContextRegistration> _contextSources = [];

    // Pipeline storage
    private readonly List<PipelineMiddleware<ToolCall, Task<ToolResult>>> _toolMiddlewares = [];
    private readonly List<PipelineMiddleware<LLMCall, IAsyncEnumerable<LLMEvent>>> _llmMiddlewares = [];

    public AgentBuilder() { }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithInstructions(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }
    public AgentBuilder WithTools(Action<ToolRegistry> configure) { _toolRegistrations.Add(configure); return this; }
    public AgentBuilder WithToolOptions(Action<ToolOptions> configure) { configure(_config.ToolOptions); return this; }

    public AgentBuilder WithMemory(IAgentMemory memory) { Memory = memory; return this; }
    public AgentBuilder WithMemory(IMemory knowledge) { KnowledgeMemory = knowledge; return this; }
    public AgentBuilder WithContextCompactor(IContextCompactor compactor) { ContextCompactor = compactor; return this; }
    public AgentBuilder WithContextAssembler(IContextAssembler assembler) { ContextAssembler = assembler; return this; }
    
    public AgentBuilder AddContext(IContextSource source, int? maxTokenBudget = null) 
    { 
        _contextSources.Add(new ContextRegistration(source, maxTokenBudget)); 
        return this; 
    }

    public AgentBuilder AddContext(string name, string text, Role role = Role.System, int? maxTokenBudget = null, int priority = 50)
    {
        _contextSources.Add(new ContextRegistration(new StaticContextSource(name, text, role, priority), maxTokenBudget));
        return this;
    }

    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { TokenCounter = tokenCounter; return this; }
    public AgentBuilder WithTokenManager(ITokenManager tokenManager) { TokenManager = tokenManager; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory) { LoggerFactory = loggerFactory; return this; }
    public AgentBuilder WithProvider(ILLMProvider provider, LLMOptions? options = null) 
    { 
        Provider = provider; 
        if (options != null) _providerOptions = options;
        return this; 
    }

    // Executor pipelines
    public AgentBuilder UseToolMiddleware(PipelineMiddleware<ToolCall, Task<ToolResult>> middleware) { _toolMiddlewares.Add(middleware); return this; }
    public AgentBuilder UseLLMMiddleware(PipelineMiddleware<LLMCall, IAsyncEnumerable<LLMEvent>> middleware) { _llmMiddlewares.Add(middleware); return this; }

    public LLMAgent Build()
    {
        if (Provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call AddOpenAI().");

        var loggerFactory = LoggerFactory ?? NullLoggerFactory.Instance;
        var memory = Memory ?? new InMemoryMemory();
        var tokenCounter = TokenCounter ?? new ApproximateTokenCounter();
        var contextCompactor = ContextCompactor ?? new SummarizingContextCompactor(tokenCounter, loggerFactory.CreateLogger<SummarizingContextCompactor>(), Provider);
        var tokenManager = TokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        var assembler = ContextAssembler ?? new SimpleContextAssembler();
        foreach (var reg in _contextSources)
            assembler.Register(reg.Source, reg.MaxTokenBudget);

        if (_config.SystemPrompt != null)
            assembler.Register(new StaticContextSource("instructions", _config.SystemPrompt, Role.System, 100));

        if (KnowledgeMemory != null)
        {
            assembler.Register(KnowledgeMemory);
            _toolRegistrations.Add(r => r.RegisterAll(new KnowledgeTools(KnowledgeMemory)));
        }

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);

        var toolExecutor = new ToolExecutor(
            registry, 
            _config.ToolOptions,
            loggerFactory.CreateLogger<ToolExecutor>(),
            _toolMiddlewares);

        var llmExecutor = new LLMExecutor(
            Provider,
            registry,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>(),
            _llmMiddlewares);

        return new LLMAgent(
            memory,
            llmExecutor,
            toolExecutor,
            contextCompactor,
            assembler,
            tokenCounter,
            _providerOptions ?? new LLMOptions(),
            _config,
            loggerFactory.CreateLogger<LLMAgent>());
    } 
}
