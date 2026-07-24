using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Exceptions;
using AgentCore.LLM.Schema;
using AgentCore.Context;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System;
using System.Linq;

namespace AgentCore
{
    public interface IAgentWorkflow
    {
        IAsyncEnumerable<IContent> ExecuteAsync(
            IContext context,
            IContent input,
            JsonSchema? responseSchema,
            CancellationToken ct = default);
    }

    public class ReActWorkflow : IAgentWorkflow
    {
        private readonly ILLM _llm;
        private readonly ITooling _tooling;
        private readonly int? _maxIterations;
        private readonly ILogger<ReActWorkflow>? _logger;

        public ReActWorkflow(
            ILLM llm,
            ITooling tooling,
            int? maxIterations = null,
            ILogger<ReActWorkflow>? logger = null)
        {
            _llm = llm;
            _tooling = tooling;
            _maxIterations = maxIterations;
            _logger = logger;
        }

        public async IAsyncEnumerable<IContent> ExecuteAsync(
            IContext context,
            IContent input,
            JsonSchema? responseSchema,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            int iterations = 0;

            // 1. Immediately record user input in the context (durable)
            var userMessage = new Message(Role.User, input);
            await context.AddAsync(userMessage, ct).ConfigureAwait(false);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (_maxIterations.HasValue && iterations >= _maxIterations.Value)
                {
                    _logger?.LogError("Execution exceeded the maximum limit of {MaxIterations} iterations.", _maxIterations.Value);
                    throw new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations.");
                }

                var currentMessages = context.Messages;
                _logger?.LogDebug("Starting execution iteration {Iteration} (Conversation message count: {MessageCount}).", iterations, currentMessages.Count);

                var options = new LLMOptions { ResponseSchema = responseSchema };
                _logger?.LogDebug("Calling LLM StreamAsync...");
                
                var (assistantMessage, metadata) = await _llm
                    .StreamAsync(currentMessages, options, _tooling.Tools, ct)
                    .AccumulateAsync(ct)
                    .ConfigureAwait(false);

                if (assistantMessage == null)
                {
                    _logger?.LogWarning("LLM returned null response.");
                    break;
                }

                // 2. Save LLM response to context immediately (durable mid-turn!)
                await context.AddAsync(assistantMessage, ct).ConfigureAwait(false);

                // Yield all contents produced by LLM assistant response (Text, Reasoning, ToolCall)
                foreach (var content in assistantMessage.Contents)
                {
                    yield return content;
                }

                var toolCalls = assistantMessage.Contents.OfType<ToolCall>().ToList();
                if (toolCalls.Count > 0)
                {
                    iterations++;

                    _logger?.LogDebug("Executing {ToolCount} tool calls...", toolCalls.Count);
                    var toolMessages = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);

                    // 3. Save tool results to context immediately and yield ToolResult semantic content
                    foreach (var message in toolMessages)
                    {
                        await context.AddAsync(message, ct).ConfigureAwait(false);
                        foreach (var result in message.Contents.OfType<ToolResult>())
                        {
                            yield return result;
                        }
                    }

                    continue;
                }

                break;
            }
        }
    }
}
