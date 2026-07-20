using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Memory;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore;

public sealed partial class Agent
{
    /// <summary>
    /// Builder to configure and create an Agent instance.
    /// NOTE: Each Build() call produces mutable, stateful components (like RollingWindowMemory)
    /// scoped to a single conversation/session. Do not share or reuse an Agent instance across concurrent,
    /// unrelated conversations.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<Tool> _tools = [];
        private ILogger<Builder> _logger;
        private IContent? _instructions;

        private IMemory? _memory;
        private IToolService? _tooling;
        private ITokenCounter? _tokenCounter;
        private ILoggerFactory? _loggerFactory;
        private ILLM? _provider;
        private Func<ILLM, IToolService, IAgentWorkflow>? _workflowFactory;

        private readonly List<Func<IToolService, IToolService>> _toolingLayers = [];

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

        public Builder WithMemory(IMemory memory) { _memory = memory; return this; }

        public Builder UseTooling(IToolService tooling) { _tooling = tooling; return this; }
        public Builder AddToolingLayer(Func<IToolService, IToolService> layer) { _toolingLayers.Add(layer); return this; }

        public Builder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
        
        public Builder WithLoggerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<Builder>() ?? NullLogger<Builder>.Instance;
            return this;
        }
        
        public Builder WithProvider(ILLM provider) { _provider = provider; return this; }

        public Builder UseWorkflow(Func<ILLM, IToolService, IAgentWorkflow> factory)
        {
            _workflowFactory = factory;
            return this;
        }

        public Agent Build()
        {
            _logger.LogInformation("Agent build started");

            var baseProvider = _provider;
            if (baseProvider == null)
                throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.Providers.Tornado) and call WithProvider().");

            var lf = _loggerFactory ?? NullLoggerFactory.Instance;
            var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter(logger: lf.CreateLogger<ApproximateTokenCounter>());

            var frozenTools = _tools.ToArray();
            _logger.LogDebug("Tool registration: TotalTools={ToolCount}", frozenTools.Length);

            IToolService tooling = _tooling ?? new ToolService(frozenTools, lf.CreateLogger<ToolService>());
            foreach (var layer in _toolingLayers)
                tooling = layer(tooling);

            var capabilities = baseProvider.GetCapabilities();

            IMemory memory = _memory ?? new ChatMemory(
                tokenCounter,
                capabilities,
                frozenTools,
                _instructions,
                summarizer: baseProvider);

            _logger.LogInformation("Agent build completed: Tools={ToolCount} ProviderType={ProviderType}",
                frozenTools.Length,
                baseProvider.GetType().Name);

            var workflow = _workflowFactory != null 
                ? _workflowFactory(baseProvider, tooling) 
                : new ReActWorkflow(baseProvider, tooling);

            return new Agent(baseProvider, memory, frozenTools, _instructions, workflow, lf.CreateLogger<Agent>());
        }
    }
}
