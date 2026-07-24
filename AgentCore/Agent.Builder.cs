using System;
using System.Collections.Generic;
using System.Linq;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Context;
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

        private IContext? _context;
        private ITooling? _tooling;
        private ITokenCounter? _tokenCounter;
        private ILoggerFactory? _loggerFactory;
        private ILLM? _provider;
        private Func<ILLM, ITooling, IAgentWorkflow>? _workflowFactory;

        private readonly List<ToolingLayer> _toolingLayers = [];
        private readonly List<LLMLayer> _llmLayers = [];
        private readonly List<ContextLayer> _contextLayers = [];

        private readonly List<object> _builtComponents = new();

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

        public Builder WithTool(Tool tool)
        {
            AddTool(tool);
            return this;
        }

        public Builder WithTools(IEnumerable<Tool> tools)
        {
            ArgumentNullException.ThrowIfNull(tools);
            foreach (var tool in tools)
                AddTool(tool);
            return this;
        }

        public Builder WithContext(IContext context) { _context = context; return this; }
        public Builder AddContextLayer(ContextLayer layer) { _contextLayers.Add(layer); return this; }

        public Builder WithTooling(ITooling tooling) { _tooling = tooling; return this; }
        public Builder AddToolingLayer(ToolingLayer layer) { _toolingLayers.Add(layer); return this; }

        public Builder WithTokenCounter(ITokenCounter tokenCounter) { _tokenCounter = tokenCounter; return this; }
        
        public Builder WithLoggerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<Builder>() ?? NullLogger<Builder>.Instance;
            return this;
        }
        
        public Builder WithLLM(ILLM provider) { _provider = provider; return this; }
        public Builder AddLLMLayer(LLMLayer layer) { _llmLayers.Add(layer); return this; }

        public Builder WithWorkflow(Func<ILLM, ITooling, IAgentWorkflow> factory)
        {
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
            _logger.LogInformation("Agent build started");
            _builtComponents.Clear();

            var baseProvider = _provider;
            if (baseProvider == null)
                throw new InvalidOperationException("No LLM provider registered. Install a provider package (e.g., AgentCore.Providers.Tornado) and call WithProvider().");

            var lf = _loggerFactory ?? NullLoggerFactory.Instance;
            var tokenCounter = _tokenCounter ?? new ApproximateTokenCounter(logger: lf.CreateLogger<ApproximateTokenCounter>());

            ILLM provider = baseProvider;
            foreach (var layer in _llmLayers)
            {
                layer.Attach(provider);
                provider = layer;
            }

            var calibrationLayer = new TokenCalibrationLayer(tokenCounter);
            calibrationLayer.Attach(provider);
            provider = calibrationLayer;

            var frozenTools = _tools.ToArray();
            _logger.LogDebug("Tool registration: TotalTools={ToolCount}", frozenTools.Length);

            ITooling tooling = _tooling ?? new Tooling(frozenTools, lf.CreateLogger<Tooling>());
            foreach (var layer in _toolingLayers)
            {
                layer.Attach(tooling);
                tooling = layer;
            }

            var capabilities = baseProvider.GetCapabilities();

            IContext memory = _context ?? new ChatContext(
                tokenCounter,
                capabilities,
                frozenTools,
                _instructions,
                summarizer: baseProvider,
                logger: lf.CreateLogger<ChatContext>());

            foreach (var layer in _contextLayers)
            {
                layer.Attach(memory);
                memory = layer;
            }

            _logger.LogInformation("Agent build completed: Tools={ToolCount} ProviderType={ProviderType}",
                frozenTools.Length,
                provider.GetType().Name);

            var workflow = _workflowFactory != null 
                ? _workflowFactory(provider, tooling) 
                : new ReActWorkflow(provider, tooling, logger: lf.CreateLogger<ReActWorkflow>());

            _builtComponents.Add(provider);
            _builtComponents.Add(tooling);
            _builtComponents.Add(memory);

            return new Agent(provider, memory, frozenTools, _instructions, workflow, lf.CreateLogger<Agent>());
        }
    }
}
