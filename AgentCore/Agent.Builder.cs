using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Memory;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public sealed partial class Agent
{
    public sealed class Builder
    {
        private readonly List<Tool> _tools = [];
        private ILogger<Builder> _logger;
        private IContent? _instructions;

        private IContextService? _contextService;
        private IMemory? _memoryProvider;
        private IToolService? _tooling;
        private ILLMService? _llm;
        private ITokenCounter? _tokenCounter;
        private ILoggerFactory? _loggerFactory;
        private ILLM? _provider;
        private Func<ILLMService, IToolService, IAgentWorkflow>? _workflowFactory;

        private readonly List<Func<IContextService, IContextService>> _contextLayers = [];
        private readonly List<Func<IToolService, IToolService>> _toolingLayers = [];
        private readonly List<Func<ILLMService, ILLMService>> _llmLayers = [];

        public Builder()
        {
            _logger = NullLogger<Builder>.Instance;
        }

        public Builder WithInstructions(string prompt) { _instructions = new Text(prompt); return this; }

        private void AddTool(Tool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (_tools.Any(t => string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Duplicate tool name '{tool.Name}'.");
            }
            _tools.Add(tool);
        }

        public Builder WithTools<T>()
        {
            foreach (var tool in MethodTool.FromType(typeof(T)))
                AddTool(tool);
            return this;
        }

        public Builder WithTools(object instance)
        {
            ArgumentNullException.ThrowIfNull(instance);
            foreach (var tool in MethodTool.FromType(instance.GetType(), instance))
                AddTool(tool);
            return this;
        }

        public Builder UseContext(IContextService contextService) { _contextService = contextService; return this; } 
        public Builder AddContextLayer(Func<IContextService, IContextService> layer) { _contextLayers.Add(layer); return this; }

        public Builder WithMemory(IMemory memoryProvider) { _memoryProvider = memoryProvider; return this; }

        public Builder UseTooling(IToolService tooling) { _tooling = tooling; return this; }
        public Builder AddToolingLayer(Func<IToolService, IToolService> layer) { _toolingLayers.Add(layer); return this; }

        public Builder UseLLM(ILLMService llm) { _llm = llm; return this; }
        public Builder AddLLMLayer(Func<ILLMService, ILLMService> layer) { _llmLayers.Add(layer); return this; }

        public Builder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
        
        public Builder WithLoggerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<Builder>() ?? NullLogger<Builder>.Instance;
            return this;
        }
        
        public Builder WithProvider(ILLM provider) { _provider = provider; return this; }

        public Builder UseWorkflow(Func<ILLMService, IToolService, IAgentWorkflow> factory)
        {
            _workflowFactory = factory;
            return this;
        }

        public Agent Build()
        {
            _logger.LogInformation("Agent build started");

            var baseProvider = _provider;
            if (baseProvider == null && _llm == null)
                throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.Providers.Tornado) and call WithProvider().");

            var lf = _loggerFactory ?? NullLoggerFactory.Instance;
            var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter(logger: lf.CreateLogger<ApproximateTokenCounter>());

            var frozenTools = _tools.ToArray();
            _logger.LogDebug("Tool registration: TotalTools={ToolCount}", frozenTools.Length);

            IToolService tooling = _tooling ?? new ToolService(frozenTools, lf.CreateLogger<ToolService>());
            foreach (var layer in _toolingLayers)
                tooling = layer(tooling);

            ILLMService pipeline;
            if (_llm != null)
            {
                pipeline = _llm;
            }
            else
            {
                pipeline = new LLMService(baseProvider!, tokenCounter, logger: lf.CreateLogger<LLMService>());
            }

            foreach (var layer in _llmLayers)
                pipeline = layer(pipeline);

            IMemory memoryProvider = _memoryProvider ?? new InMemoryMemoryProvider();
            IContextService contextService = _contextService;
            if (contextService == null)
            {
                var providerForContext = baseProvider ?? (_llm as LLMService)?.Provider;
                if (providerForContext == null)
                {
                    throw new InvalidOperationException("Cannot resolve ILLMProvider for default context service.");
                }
                contextService = new ContextService(tokenCounter, memoryProvider, providerForContext);
            }
            foreach (var layer in _contextLayers)
                contextService = layer(contextService);

            _logger.LogInformation("Agent build completed: Tools={ToolCount} ProviderType={ProviderType}",
                frozenTools.Length,
                _llm != null ? _llm.GetType().Name : baseProvider!.GetType().Name);

            var workflow = _workflowFactory != null 
                ? _workflowFactory(pipeline, tooling) 
                : new ReActWorkflow(pipeline, tooling);

            return new Agent(pipeline, contextService, frozenTools, _instructions, workflow, lf.CreateLogger<Agent>());
        }
    }
}
