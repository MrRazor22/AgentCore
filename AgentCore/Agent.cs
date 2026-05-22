using AgentCore.Conversation;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using AgentCore.Context;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore;

public interface IAgent
{
    Task<AgentResponse> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, string? sessionId = null, CancellationToken ct = default);
    Task<AgentResponse> ResumeAsync(string sessionId, string toolCallId, bool approved, CancellationToken ct = default);
}

public sealed class LLMAgent : IAgent
{
    private readonly IChatMemory _chatStore;
    private readonly IAgentMemory? _memory;
    private readonly ILLMExecutor _llm;
    private readonly IToolExecutor _toolRuntime;
    private readonly IContextManager _contextManager;
    private readonly ITokenCounter _tokenCounter;
    private readonly IAgentRuntime _agentRuntime;
    private readonly LLMOptions _baseOptions;
    private readonly IReadOnlyList<Tool> _toolsCollection;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger;
    private readonly IReadOnlyList<IMiddleware<AgentRequestContext, AgentResponse>> _unaryMiddlewares;
    private readonly IReadOnlyList<IMiddleware<AgentRequestContext, IAsyncEnumerable<AgentEvent>>> _streamingMiddlewares;

    public LLMAgent(
        IChatMemory chatStore,
        ILLMExecutor llm,
        IToolExecutor toolRuntime,
        IContextManager contextManager,
        IAgentMemory? memory,
        ITokenCounter tokenCounter,
        IAgentRuntime agentRuntime,
        LLMOptions baseOptions,
        IReadOnlyList<Tool> toolsCollection,
        AgentConfig config,
        ILogger<LLMAgent> logger,
        IReadOnlyList<IMiddleware<AgentRequestContext, AgentResponse>> unaryMiddlewares,
        IReadOnlyList<IMiddleware<AgentRequestContext, IAsyncEnumerable<AgentEvent>>> streamingMiddlewares)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _toolRuntime = toolRuntime ?? throw new ArgumentNullException(nameof(toolRuntime));
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _memory = memory;
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
        _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));
        _toolsCollection = toolsCollection ?? throw new ArgumentNullException(nameof(toolsCollection));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _unaryMiddlewares = unaryMiddlewares ?? throw new ArgumentNullException(nameof(unaryMiddlewares));
        _streamingMiddlewares = streamingMiddlewares ?? throw new ArgumentNullException(nameof(streamingMiddlewares));
    }

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public async Task<AgentResponse> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString("N");
        var chat = await _chatStore.RecallAsync(sessionId);
        var requestContext = new AgentRequestContext(input, sessionId, chat.AsReadOnly());

        var pipeline = new MiddlewarePipeline<AgentRequestContext, AgentResponse>((ctx, innerCt) =>
            InvokeAsyncInternal(ctx.Input, ctx.SessionId, null, innerCt));

        foreach (var mw in _unaryMiddlewares)
        {
            pipeline.Use(mw);
        }

        return await pipeline.InvokeAsyncWithTerminal(requestContext, ct);
    }

    public async Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString("N");
        var chat = await _chatStore.RecallAsync(sessionId);
        var requestContext = new AgentRequestContext(input, sessionId, chat.AsReadOnly());

        var pipeline = new MiddlewarePipeline<AgentRequestContext, AgentResponse>((ctx, innerCt) =>
            InvokeAsyncInternal(ctx.Input, ctx.SessionId, typeof(T), innerCt));

        foreach (var mw in _unaryMiddlewares)
        {
            pipeline.Use(mw);
        }

        var response = await pipeline.InvokeAsyncWithTerminal(requestContext, ct);
        var json = response.Text;
        
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    private async Task<AgentResponse> InvokeAsyncInternal(
        IContent input, 
        string sessionId, 
        Type? outputType, 
        CancellationToken ct)
    {
        var chatBefore = await _chatStore.RecallAsync(sessionId);
        int startIndex = chatBefore.Count;

        int inTokens = 0;
        int outTokens = 0;
        int reasoningTokens = 0;

        await foreach (var evt in CoreStreamAsync(input, sessionId, outputType, ct))
        {
            if (evt is LLMMetaEvent meta)
            {
                inTokens += meta.Usage.InputTokens;
                outTokens += meta.Usage.OutputTokens;
                reasoningTokens += meta.Usage.ReasoningTokens;
            }
        }

        var chatAfter = await _chatStore.RecallAsync(sessionId);
        var turnMessages = chatAfter.Skip(startIndex).ToList();

        var response = new AgentResponse(
            sessionId,
            turnMessages,
            new TokenUsage(inTokens, outTokens, reasoningTokens));

        return response;
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString("N");

        var streamTask = Task.Run(async () =>
        {
            var chat = await _chatStore.RecallAsync(sessionId);
            var requestContext = new AgentRequestContext(input, sessionId, chat.AsReadOnly());

            var pipeline = new MiddlewarePipeline<AgentRequestContext, IAsyncEnumerable<AgentEvent>>((ctx, innerCt) =>
                Task.FromResult(CoreStreamAsync(ctx.Input, ctx.SessionId, null, innerCt)));

            foreach (var mw in _streamingMiddlewares)
            {
                pipeline.Use(mw);
            }

            return await pipeline.InvokeAsyncWithTerminal(requestContext, ct);
        });

        return new AsyncEnumerableWrapper(streamTask);
    }

    public async Task<AgentResponse> ResumeAsync(string sessionId, string toolCallId, bool approved, CancellationToken ct = default)
    {
        _logger.LogInformation("Resume called: Session={SessionId} ToolCallId={ToolCallId} Approved={Approved}", sessionId, toolCallId, approved);

        var chat = await _chatStore.RecallAsync(sessionId);
        var updated = false;

        // Find and update the ToolCall with matching Id
        for (int i = 0; i < chat.Count; i++)
        {
            var message = chat[i];
            if (message.Role == Role.Assistant)
            {
                var updatedContents = new List<IContent>();
                foreach (var content in message.Contents)
                {
                    if (content is ToolCall tc && tc.Id == toolCallId)
                    {
                        var updatedCall = tc with { IsApproved = approved };
                        updatedContents.Add(updatedCall);
                        updated = true;
                        _logger.LogInformation("Updated ToolCall approval: {ToolCallId} IsApproved={IsApproved}", toolCallId, updatedCall.IsApproved);
                    }
                    else
                    {
                        updatedContents.Add(content);
                    }
                }

                if (updated)
                {
                    chat[i] = new Message(Role.Assistant, updatedContents);
                    break;
                }
            }
        }

        if (!updated)
        {
            _logger.LogWarning("ToolCall not found: {ToolCallId}", toolCallId);
            throw new InvalidOperationException($"ToolCall with ID '{toolCallId}' not found in session '{sessionId}'");
        }

        await _chatStore.RetainAsync(sessionId, chat);

        // Resume execution from where we left off
        return await InvokeAsyncInternal(new Text(""), sessionId, null, ct);
    }

    private LLMOptions BuildLLMOptions(Type? outputType)
    {
        return new LLMOptions
        {
            Model = _baseOptions.Model,
            ApiKey = _baseOptions.ApiKey,
            BaseUrl = _baseOptions.BaseUrl,
            ContextLength = _baseOptions.ContextLength,
            Temperature = _baseOptions.Temperature,
            TopP = _baseOptions.TopP,
            MaxOutputTokens = _baseOptions.MaxOutputTokens,
            Seed = _baseOptions.Seed,
            StopSequences = _baseOptions.StopSequences,
            FrequencyPenalty = _baseOptions.FrequencyPenalty,
            PresencePenalty = _baseOptions.PresencePenalty,
            ToolCallMode = ToolCallMode.Auto,
            ResponseSchema = outputType?.GetSchemaForType()
        };
    }

    private async IAsyncEnumerable<AgentEvent> CoreStreamAsync(
        IContent input,
        string sessionId,
        Type? outputType,
        [EnumeratorCancellation] CancellationToken ct)
    {


        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Agent"] = _config.Name,
            ["Session"] = sessionId
        }))
        {
            var chat = await _chatStore.RecallAsync(sessionId);
            var isNewSession = chat.Count == 0;
            _logger.LogInformation("Agent invoked: Session={SessionId} InputLength={Len} NewSession={IsNew} MemoryType={MemType} ContextLimit={CtxLimit}",
                sessionId, input.ForLlm().Length, isNewSession, _memory?.GetType().Name ?? "None", _baseOptions.ContextLength ?? 0);
            
            // Strictly User input is added and persisted here
            var userMessage = new Message(Role.User, input);
            chat.Add(userMessage);
            await _chatStore.RetainAsync(sessionId, chat);

            var options = BuildLLMOptions(outputType);
            var boundLlm = new ConfiguredLLMExecutor(_llm, options, _toolsCollection);

            // Execute the pluggable orchestration runtime loop on the mutable state
            await foreach (var evt in _agentRuntime.RunAsync(
                chat,
                boundLlm,
                _toolRuntime,
                ct))
            {
                await _chatStore.RetainAsync(sessionId, chat);
                yield return evt;
            }

            // Persist the final state
            await _chatStore.RetainAsync(sessionId, chat);
        }
    }

    private sealed class AsyncEnumerableWrapper : IAsyncEnumerable<AgentEvent>
    {
        private readonly Task<IAsyncEnumerable<AgentEvent>> _streamTask;

        public AsyncEnumerableWrapper(Task<IAsyncEnumerable<AgentEvent>> streamTask)
        {
            _streamTask = streamTask;
        }

        public async IAsyncEnumerator<AgentEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var stream = await _streamTask.ConfigureAwait(false);
            await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }
}
