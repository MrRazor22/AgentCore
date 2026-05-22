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
        ILogger<LLMAgent> logger)
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
    }

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public async Task<AgentResponse> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString("N");
        return await InvokeAsyncInternal(input, sessionId, ct);
    }

    public async Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        var response = await InvokeAsync(input, sessionId, ct);
        var json = response.Text;
        
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    private async Task<AgentResponse> InvokeAsyncInternal(
        IContent input, 
        string sessionId, 
        CancellationToken ct)
    {
        var chatBefore = await _chatStore.RecallAsync(sessionId);
        int startIndex = chatBefore.Count;

        int inTokens = 0;
        int outTokens = 0;
        int reasoningTokens = 0;

        await foreach (var evt in CoreStreamAsync(input, sessionId, ct))
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
        return CoreStreamAsync(input, sessionId, ct);
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
        return await InvokeAsyncInternal(new Text(""), sessionId, ct);
    }

    private async IAsyncEnumerable<AgentEvent> CoreStreamAsync(
        IContent input,
        string sessionId,
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
            
            if (isNewSession && !string.IsNullOrEmpty(_config.SystemPrompt))
            {
                chat.Add(new Message(Role.System, new Text(_config.SystemPrompt)));
            }

            // Strictly User input is added and persisted here
            var userMessage = new Message(Role.User, input);
            chat.Add(userMessage);
            await _chatStore.RetainAsync(sessionId, chat);


            // Execute the pluggable orchestration runtime loop on the mutable state
            await foreach (var evt in _agentRuntime.RunAsync(
                chat,
                _llm,
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
}
