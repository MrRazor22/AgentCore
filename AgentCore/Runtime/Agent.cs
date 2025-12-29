using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Client;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    // === 1. Context ===
    public interface IAgentContext
    {
        Conversation ScratchPad { get; }
        string? UserRequest { get; set; }
        AgentResponse Response { get; }
        IServiceProvider Services { get; }
        IDictionary<string, object?> Items { get; }
        Action<object>? Stream { get; set; }
        CancellationToken CancellationToken { get; }
    }

    public sealed class AgentResponse
    {
        public string? Message { get; private set; }
        public object? Payload { get; private set; }

        public void Set(string? message = null, object? payload = null)
        {
            Message = message;
            Payload = payload;
        }
    }

    public sealed class AgentContext : IAgentContext
    {
        private readonly AgentResponse _response = new AgentResponse();

        public AgentContext(IServiceProvider services, CancellationToken ct = default)
        {
            Services = services;
            CancellationToken = ct;
            ScratchPad = new Conversation();
            Items = new Dictionary<string, object?>();
        }

        public Conversation ScratchPad { get; }
        public string? UserRequest { get; set; }
        public AgentResponse Response => _response;
        public IServiceProvider Services { get; }
        public IDictionary<string, object?> Items { get; }
        public Action<object>? Stream { get; set; }
        public CancellationToken CancellationToken { get; }
        public void SetResult(string? message, object? payload) => _response.Set(message, payload);
    }

    // === 2. AgentBuilder ===
    public sealed class AgentBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public AgentBuilder()
        {
            Services.AddSingleton<IAgentMemory, FileMemory>();

            // Tools
            Services.AddSingleton<ToolRegistryCatalog>();
            Services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
            Services.AddSingleton<IToolCatalog>(sp => sp.GetRequiredService<ToolRegistryCatalog>());
            Services.AddSingleton<IToolRuntime, ToolRuntime>();
            Services.AddSingleton<IToolCallParser, ToolCallParser>();

            // Token + context
            Services.AddSingleton<ITokenManager, TokenManager>();
            Services.AddSingleton<IContextManager>(sp =>
                new ContextManager(
                    new ContextBudgetOptions(),
                    sp.GetRequiredService<ITokenManager>(),
                    sp.GetRequiredService<ILogger<ContextManager>>()
                )
            );

            // Retry 
            Services.AddSingleton<IRetryPolicy, RetryPolicy>();

            // Handlers
            // Handlers (stream processors)
            Services.AddTransient<TextHandler>();
            Services.AddTransient<ToolCallHandler>();
            Services.AddTransient<StructuredHandler>();
            Services.AddTransient<FinishHandler>();
            Services.AddTransient<TokenUsageHandler>();

            Services.AddTransient<IEnumerable<IChunkHandler>>(sp => new IChunkHandler[]
            {
                sp.GetRequiredService<TextHandler>(),
                sp.GetRequiredService<ToolCallHandler>(),
                sp.GetRequiredService<StructuredHandler>(),
                sp.GetRequiredService<FinishHandler>(),
                sp.GetRequiredService<TokenUsageHandler>()
            });

            //Logging
            Services.AddLogging(b => b.AddSimpleConsole(o =>
            {
                o.SingleLine = false;
                o.TimestampFormat = "hh:mm:ss ";
            }));
            Services.Configure<LoggerFilterOptions>(o =>
            {
                o.MinLevel = LogLevel.Trace;   // global output level for ILogger<T>
            });

            //Executor
            Services.AddSingleton<IAgentExecutor, ToolCallingLoop>();
        }

        public Agent Build() => Build("default");

        public Agent Build(string sessionId)
        {
            var provider = Services.BuildServiceProvider(validateScopes: true);

            if (provider.GetService<ILLMClient>() == null)
                Console.WriteLine("[Warning] No LLM client registered.");

            return new Agent(provider, sessionId);
        }
    }

    // === 3. Agent ===
    public interface IAgent
    {
        string SessionId { get; }
        Task<AgentResponse> InvokeAsync(string goal, CancellationToken ct = default, Action<object>? stream = null);
    }

    public class Agent : IAgent
    {
        private readonly IServiceProvider _services;
        private readonly IAgentMemory _memory;
        private Func<IAgentExecutor> _executorFactory;
        private string? _systemPrompt;

        public IServiceProvider Services => _services;
        public string SessionId { get; }

        internal Agent(IServiceProvider services, string sessionId)
        {
            _services = services;
            SessionId = sessionId;
            _memory = services.GetService<IAgentMemory>() ?? throw new InvalidOperationException("No memory registered.");
            _executorFactory = () => services.GetRequiredService<IAgentExecutor>();
        }

        public Agent WithTools<T>()
        {
            _services.GetRequiredService<IToolRegistry>().RegisterAll<T>();
            return this;
        }

        public Agent WithTools<T>(T instance)
        {
            _services.GetRequiredService<IToolRegistry>().RegisterAll(instance);
            return this;
        }

        public Agent WithInstructions(string prompt)
        {
            _systemPrompt = prompt;
            return this;
        }

        public Agent UseExecutor(Func<IAgentExecutor> factory)
        {
            _executorFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public virtual async Task<AgentResponse> InvokeAsync(
            string goal,
            CancellationToken ct = default,
            Action<object>? stream = null)
        {
            using var scope = _services.CreateScope();
            var ctx = new AgentContext(scope.ServiceProvider, ct)
            {
                UserRequest = goal,
                Stream = stream
            };

            try
            {
                // Build scratchpad: system prompt first, then memory
                ctx.ScratchPad.AddSystem(_systemPrompt);
                var memory = await _memory.RecallAsync(SessionId, goal).ConfigureAwait(false);
                ctx.ScratchPad.Append(memory);

                // Execute
                var executor = _executorFactory();
                await executor.ExecuteAsync(ctx).ConfigureAwait(false);

            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // DO NOT overwrite partial text
                // Just let ctx.Response stay as produced by executor
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetService<ILogger<Agent>>();
                logger?.LogError(ex, "Agent execution failed");

                // overwrite response on *actual* errors
                ctx.SetResult($"Error: {ex.Message}", null);
            }
            finally
            {
                // ALWAYS update memory — success, partial, or error
                await _memory.UpdateAsync(SessionId, goal, ctx.Response.Message)
                             .ConfigureAwait(false);
            }

            return ctx.Response;
        }

        public static AgentBuilder CreateBuilder() => new AgentBuilder();
    }
}