using AgentCore.Conversation;
using AgentCore.Schema;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.LLM;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public sealed partial class Agent
{
    public sealed class Builder
    {
        private readonly ToolRegistry _registry = new();
        private ILogger<Builder> _logger;
        private IContent? _instructions;

        private IMemoryService? _memory;
        private IToolService? _tooling;
        private ILLMService? _llm;
        private ITokenCounter? _tokenCounter;
        private ILoggerFactory? _loggerFactory;
        private ILLMProvider? _provider;
        private Func<ILLMService, IToolService, IAgentWorkflow>? _workflowFactory;

        private readonly List<Func<IMemoryService, IMemoryService>> _memoryLayers = [];
        private readonly List<Func<IToolService, IToolService>> _toolingLayers = [];
        private readonly List<Func<ILLMService, ILLMService>> _llmLayers = [];

        public Builder()
        {
            _logger = NullLogger<Builder>.Instance;
        }

        public Builder WithInstructions(string prompt) { _instructions = new Text(prompt); return this; }

        public Builder WithTools<T>()
        {
            foreach (var tool in MethodTool.FromType(typeof(T)))
                _registry.Add(tool);
            return this;
        }

        public Builder WithTools(object instance)
        {
            ArgumentNullException.ThrowIfNull(instance);
            foreach (var tool in MethodTool.FromType(instance.GetType(), instance))
                _registry.Add(tool);
            return this;
        }

        public Builder UseMemory(IMemoryService memory) { _memory = memory; return this; } 
        public Builder AddMemoryLayer(Func<IMemoryService, IMemoryService> layer) { _memoryLayers.Add(layer); return this; }

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
        
        public Builder WithProvider(ILLMProvider provider) { _provider = provider; return this; }

        public Builder UseWorkflow(Func<ILLMService, IToolService, IAgentWorkflow> factory)
        {
            _workflowFactory = factory;
            return this;
        }

        public Agent Build()
        {
            _logger.LogInformation("Agent build started");

            if (_provider == null)
                throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.OpenAI) and call WithProvider().");

            var lf = _loggerFactory ?? NullLoggerFactory.Instance;
            var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter(logger: lf.CreateLogger<ApproximateTokenCounter>());

            IMemoryService memory = _memory ?? new ChatMemoryService(tokenCounter, _provider);
            foreach (var layer in _memoryLayers)
                memory = layer(memory);

            _logger.LogDebug("Tool registration: TotalTools={ToolCount}", _registry.Tools.Count);

            IToolService tooling = _tooling ?? new ToolService(_registry, lf.CreateLogger<ToolService>());
            foreach (var layer in _toolingLayers)
                tooling = layer(tooling);

            ILLMService llm = _llm ?? new LLMService(_provider, _registry, tokenCounter, logger: lf.CreateLogger<LLMService>());
            foreach (var layer in _llmLayers)
                llm = layer(llm);

            _logger.LogInformation("Agent build completed: Tools={ToolCount} ProviderType={ProviderType}",
                _registry.Tools.Count,
                _provider.GetType().Name);

            var workflow = _workflowFactory != null 
                ? _workflowFactory(llm, tooling) 
                : new ReActWorkflow(llm, tooling);

            return new Agent(llm, tooling, memory, _instructions, workflow, lf.CreateLogger<Agent>());
        }
    }
}
