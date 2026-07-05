using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public sealed class AgentConfig
{
    public string Name { get; set; } = "agent";
    public string? SystemPrompt { get; set; }
    public int? MaxToolCalls { get; set; } = null;
}

public sealed class AgentBuilder
{
    private readonly AgentConfig _config = new();
    private readonly List<Action<ToolRegistry>> _toolRegistrations = [];
    private ILogger<AgentBuilder> _logger;

    private IMemory? _memory;
    private IToolExecutor? _toolExecutor;
    private ILLMExecutor? _llmExecutor;
    private ITokenCounter? _tokenCounter;
    private ITokenManager? _tokenManager;
    private ILoggerFactory? _loggerFactory;
    private ILLMProvider? _provider;
    private LLMOptions? _providerOptions;

    private readonly List<Func<IMemory, IMemory>> _memoryLayers = [];
    private readonly List<Func<IToolExecutor, IToolExecutor>> _toolExecutorLayers = [];
    private readonly List<Func<ILLMExecutor, ILLMExecutor>> _llmExecutorLayers = [];

    private readonly List<Skill> _skills = [];

    public AgentBuilder()
    {
        _logger = NullLogger<AgentBuilder>.Instance;
    }

    public AgentBuilder WithName(string name) { _config.Name = name; return this; }
    public AgentBuilder WithSystemPrompt(string prompt) { _config.SystemPrompt = prompt; return this; }
    public AgentBuilder WithTools<T>() { _toolRegistrations.Add(r => r.RegisterAll<T>()); return this; }
    public AgentBuilder WithTools<T>(T instance) { _toolRegistrations.Add(r => r.RegisterAll(instance)); return this; }
    public AgentBuilder WithTools(Action<ToolRegistry> configure) { _toolRegistrations.Add(configure); return this; }

    public AgentBuilder UseMemory(IMemory memory) { _memory = memory; return this; }
    public AgentBuilder WithMemory(IMemory memory) => UseMemory(memory);
    public AgentBuilder AddMemoryLayer(Func<IMemory, IMemory> layer) { _memoryLayers.Add(layer); return this; }

    public AgentBuilder UseToolExecutor(IToolExecutor toolExecutor) { _toolExecutor = toolExecutor; return this; }
    public AgentBuilder AddToolExecutorLayer(Func<IToolExecutor, IToolExecutor> layer) { _toolExecutorLayers.Add(layer); return this; }

    public AgentBuilder UseLlmExecutor(ILLMExecutor llmExecutor) { _llmExecutor = llmExecutor; return this; }
    public AgentBuilder AddLlmExecutorLayer(Func<ILLMExecutor, ILLMExecutor> layer) { _llmExecutorLayers.Add(layer); return this; }

    public AgentBuilder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
    public AgentBuilder WithTokenManager(ITokenManager tokenManager) { _tokenManager = tokenManager; return this; }
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentBuilder>() ?? NullLogger<AgentBuilder>.Instance;
        return this;
    }
    public AgentBuilder WithProvider(ILLMProvider provider, LLMOptions? options = null)
    {
        _provider = provider;
        if (options != null) _providerOptions = options;
        return this;
    }



    public AgentBuilder WithSkill(string name, string description, string content)
    {
        _skills.Add(new Skill(name, description, content));
        return this;
    }

    public AgentBuilder WithSkillsDirectory(string path)
    {
        var loadedSkills = SkillLoader.FromDirectory(path);
        _skills.AddRange(loadedSkills);
        return this;
    }

    /// <summary>
    /// Registers another agent as a tool. The LLM can call the sub-agent by name,
    /// passing a task string and receiving the agent's response.
    /// This enables multi-agent orchestration with zero infrastructure.
    /// </summary>
    public AgentBuilder WithAgentTool(IAgent agent, string name, string description)
    {
        _toolRegistrations.Add(registry =>
        {
            var inputSchema = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "object",
                ["properties"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["task"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The task or question to delegate to this agent"
                    }
                },
                ["required"] = new System.Text.Json.Nodes.JsonArray("task")
            };

            registry.Register(new Tooling.Tool
            {
                Name = name,
                Description = description,
                ParametersSchema = inputSchema,
                Invoker = async (args) =>
                {
                    var task = args.Length > 0 ? args[0]?.ToString() ?? "" : "";
                    var response = await agent.InvokeAsync(new Conversation.Text(task));
                    return new Conversation.Text(response.ForLlm());
                }
            });
        });
        return this;
    }


    public IAgent Build()
    {
        _logger.LogInformation("Agent build started: Name={AgentName}", _config.Name);

        if (_provider == null)
            throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call WithProvider().");

        var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
        var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter();
        var tokenManager = _tokenManager ?? new TokenManager(loggerFactory.CreateLogger<TokenManager>());
        
        IMemory memory = _memory ?? new ChatMemory(tokenCounter, _provider);
        foreach (var layer in _memoryLayers)
        {
            memory = layer(memory);
        }

        var registry = new ToolRegistry();
        foreach (var init in _toolRegistrations) init(registry);

        _logger.LogDebug("Tool registration: TotalTools={ToolCount}", registry.Tools.Count);



        // Register skill tools if skills are provided
        if (_skills.Count > 0)
            registry.RegisterAll(new SkillTools(_skills));

        IToolExecutor toolExecutor = _toolExecutor ?? new ToolExecutor(
            registry,
            loggerFactory.CreateLogger<ToolExecutor>());
        foreach (var layer in _toolExecutorLayers)
        {
            toolExecutor = layer(toolExecutor);
        }

        ILLMExecutor llmExecutor = _llmExecutor ?? new LLMExecutor(
            _provider,
            registry,
            tokenCounter,
            tokenManager,
            loggerFactory.CreateLogger<LLMExecutor>());
        foreach (var layer in _llmExecutorLayers)
        {
            llmExecutor = layer(llmExecutor);
        }

        _logger.LogInformation("Agent build completed: Name={AgentName} Tools={ToolCount} Skills={SkillCount} ProviderType={ProviderType}",
            _config.Name,
            registry.Tools.Count,
            _skills.Count,
            _provider.GetType().Name);

        return new LLMAgent(
            llmExecutor,
            toolExecutor,
            memory,
            tokenCounter,
            _providerOptions ?? new LLMOptions(),
            _config,
            loggerFactory.CreateLogger<LLMAgent>());
    } 
}
