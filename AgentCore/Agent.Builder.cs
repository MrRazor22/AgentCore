using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
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

        private Func<ILoggerFactory, ILLM>? _llmFactory;
        private Func<ILoggerFactory, IContext>? _contextFactory;
        private Func<ILoggerFactory, ITooling>? _toolingFactory;
        private Func<ILLM, ITooling, ILoggerFactory, IAgentWorkflow>? _workflowFactory;
        private ILoggerFactory? _loggerFactory;

        private readonly List<ToolingLayer> _toolingLayers = [];
        private readonly List<LLMLayer> _llmLayers = [];
        private readonly List<ContextLayer> _contextLayers = [];

        private readonly List<object> _builtComponents = new();

        public Builder()
        {
            _logger = NullLogger<Builder>.Instance;
        }

        public Builder WithInstructions(string prompt) { _instructions = new Text(prompt); return this; }

        public Builder WithTools(params Tool[] tools)
        {
            ArgumentNullException.ThrowIfNull(tools);
            foreach (var tool in tools)
            {
                ArgumentNullException.ThrowIfNull(tool);
                _tools.Add(tool);
            }
            return this;
        }

        public Builder WithContext(Func<ILoggerFactory, IContext> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _contextFactory = factory;
            return this;
        }

        public Builder AddContextLayer(ContextLayer layer) { _contextLayers.Add(layer); return this; }

        public Builder WithTooling(Func<ILoggerFactory, ITooling> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _toolingFactory = factory;
            return this;
        }

        public Builder AddToolingLayer(ToolingLayer layer) { _toolingLayers.Add(layer); return this; }

        public Builder WithLoggerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<Builder>() ?? NullLogger<Builder>.Instance;
            return this;
        }

        public Builder WithLLM(Func<ILoggerFactory, ILLM> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _llmFactory = factory;
            return this;
        }

        public Builder AddLLMLayer(LLMLayer layer) { _llmLayers.Add(layer); return this; }

        public Builder WithWorkflow(Func<ILLM, ITooling, ILoggerFactory, IAgentWorkflow> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _workflowFactory = factory;
            return this;
        }

        public T? GetService<T>() where T : class
        {
            return _builtComponents.OfType<T>().FirstOrDefault();
        }

        public T GetRequiredService<T>() where T : class
        {
            var service = GetService<T>();
            if (service == null)
            {
                throw new InvalidOperationException($"No built component of type '{typeof(T)}' was found.");
            }
            return service;
        }

        public Agent Build()
        {
            _builtComponents.Clear();

            var lf = _loggerFactory ?? NullLoggerFactory.Instance;

            if (_llmFactory == null)
                throw new InvalidOperationException("No LLM provider registered. Call WithLLM().");

            var baseProvider = _llmFactory(lf);

            ILLM provider = baseProvider;
            foreach (var layer in _llmLayers)
            {
                layer.Attach(provider);
                provider = layer;
            }

            var frozenTools = _tools.ToArray();

            ITooling tooling = _toolingFactory != null
                ? _toolingFactory(lf)
                : new Tooling(frozenTools, lf.CreateLogger<Tooling>());
            foreach (var layer in _toolingLayers)
            {
                layer.Attach(tooling);
                tooling = layer;
            }

            IContext memory = _contextFactory != null
                ? _contextFactory(lf)
                : new ChatContext(summarizer: baseProvider, logger: lf.CreateLogger<ChatContext>());

            foreach (var layer in _contextLayers)
            {
                layer.Attach(memory);
                memory = layer;
            }

            if (_instructions != null)
            {
                memory.StageAsync([new Message(Role.System, _instructions)]).GetAwaiter().GetResult();
            }

            var workflow = _workflowFactory != null
                ? _workflowFactory(provider, tooling, lf)
                : new ReActWorkflow(provider, tooling, logger: lf.CreateLogger<ReActWorkflow>());

            _logger.LogInformation("Agent built: Tools={ToolCount} Provider={ProviderType} Context={ContextType} Workflow={WorkflowType} LLMLayers={LLMLayers} ToolingLayers={ToolingLayers} ContextLayers={ContextLayers}",
                frozenTools.Length,
                provider.GetType().Name,
                memory.GetType().Name,
                workflow.GetType().Name,
                _llmLayers.Count,
                _toolingLayers.Count,
                _contextLayers.Count);

            _builtComponents.Add(provider);
            _builtComponents.Add(tooling);
            _builtComponents.Add(memory);

            return new Agent(memory, workflow, lf.CreateLogger<Agent>());
        }
    }
}
