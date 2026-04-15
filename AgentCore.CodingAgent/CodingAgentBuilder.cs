using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Runtime;
using AgentCore.Tooling;
using AgentCore.Tokens;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using Anthropic;
using System.ClientModel;
using System.Text;

namespace AgentCore.CodingAgent;

public enum ExecutorType
{
    Roslyn,
    Process,
}

public sealed class CodingAgentBuilder
{
    private string _name = "coding-agent";
    private string? _instructions;
    private ILLMProvider? _provider;
    private LLMOptions? _llmOptions;
    private ToolRegistry? _tools;
    private readonly List<Action<ToolRegistry>> _toolRegistrations = [];
    private SandboxPolicy _sandboxPolicy = SandboxPolicy.Restrictive;
    private ExecutorType _executorType = ExecutorType.Roslyn;
    private int _maxSteps = 20;
    private (string open, string close) _codeBlockTags = ("```csharp", "```");
    private IChatStore? _memory;
    private ITokenCounter? _tokenCounter;
    private ITokenManager? _tokenManager;
    private ILoggerFactory? _loggerFactory;

    public static CodingAgentBuilder Create(string name) => new() { _name = name };

    public CodingAgentBuilder WithName(string name) { _name = name; return this; }
    public CodingAgentBuilder WithInstructions(string? instructions) { _instructions = instructions; return this; }

    public CodingAgentBuilder AddOpenAI(string model, string? apiKey = null, string? baseUrl = null, Action<LLMOptions>? configure = null)
    {
        var options = new LLMOptions
        {
            Model = model,
            ApiKey = apiKey ?? "dummy",
            BaseUrl = baseUrl ?? "https://api.openai.com/v1",
            ToolCallMode = ToolCallMode.None,
            StopSequences = ["Observation:"],
        };
        configure?.Invoke(options);
        _llmOptions = options;

        var openAiOptions = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(baseUrl))
            openAiOptions.Endpoint = new Uri(baseUrl);
        
        var credentials = new ApiKeyCredential(apiKey ?? "dummy");
        var openAiClient = new OpenAIClient(credentials, openAiOptions);
        var chatClient = openAiClient.GetChatClient(model).AsIChatClient();
        
        _provider = new AgentCore.Providers.MEAI.MEAILLMClient(chatClient);
        
        return this;
    }

    public CodingAgentBuilder AddAnthropic(string model, string? apiKey = null, Action<LLMOptions>? configure = null)
    {
        var options = new LLMOptions
        {
            Model = model,
            ApiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            BaseUrl = "https://api.anthropic.com/v1",
            ToolCallMode = ToolCallMode.None,
            StopSequences = ["Observation:"],
        };
        configure?.Invoke(options);
        _llmOptions = options;

        var anthropicClient = new AnthropicClient { ApiKey = apiKey ?? "" };
        var chatClient = anthropicClient.AsIChatClient(model);
        _provider = new AgentCore.Providers.MEAI.MEAILLMClient(chatClient);
        
        return this;
    }

    public CodingAgentBuilder WithProvider(ILLMProvider provider, Action<LLMOptions>? configure = null)
    {
        _provider = provider;
        var options = new LLMOptions { ToolCallMode = ToolCallMode.None, StopSequences = ["Observation:"] };
        configure?.Invoke(options);
        _llmOptions = options;
        return this;
    }

    public CodingAgentBuilder WithTools<T>() where T : class
    {
        _toolRegistrations.Add(r => r.RegisterAll<T>());
        return this;
    }

    public CodingAgentBuilder WithTools<T>(T instance) where T : class
    {
        _toolRegistrations.Add(r => r.RegisterAll(instance));
        return this;
    }



    public CodingAgentBuilder WithSandbox(SandboxPolicy policy) { _sandboxPolicy = policy; return this; }
    public CodingAgentBuilder WithExecutor(ExecutorType type) { _executorType = type; return this; }
    public CodingAgentBuilder WithMaxSteps(int maxSteps) { _maxSteps = maxSteps; return this; }
    public CodingAgentBuilder WithCodeBlockTags(string open, string close) { _codeBlockTags = (open, close); return this; }
    public CodingAgentBuilder WithMemory(IChatStore memory) { _memory = memory; return this; }
    public CodingAgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
    public CodingAgentBuilder WithTokenManager(ITokenManager tokenManager) { _tokenManager = tokenManager; return this; }
    public CodingAgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory) { _loggerFactory = loggerFactory; return this; }

    public CodingAgent Build()
    {
        var toolRegistry = _tools ?? new ToolRegistry();
        foreach (var registration in _toolRegistrations) registration(toolRegistry);
        var memory = _memory ?? new InMemoryMemory();
        var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
        var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter();
        var tokenManager = _tokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());

        var llmExecutor = new LLMExecutor(
            _provider!,
            toolRegistry,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>());
        
        var executor = _executorType switch
        {
            ExecutorType.Roslyn => (ICSharpExecutor)new RoslynScriptExecutor(_sandboxPolicy),
            ExecutorType.Process => new ProcessExecutor(_sandboxPolicy),
            _ => throw new ArgumentException($"Unknown executor type: {_executorType}")
        };

        var toolExecutor = new ToolExecutor(
            toolRegistry,
            new ToolOptions(),
            loggerFactory.CreateLogger<ToolExecutor>());

        return new CodingAgent(_name, _instructions, llmExecutor, _llmOptions!, toolRegistry, executor, _sandboxPolicy, _maxSteps, _codeBlockTags, memory, toolExecutor);
    }
}
