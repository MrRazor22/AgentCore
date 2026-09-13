using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.ChatFunctions;
using LlmTornado.Code;
using LlmTornado.Responses.Events;

namespace AgentCore.LLM.Tornado;

/// <summary>
/// Adapts LLMTornado to the AgentCore ILLM event-streaming interface via direct SSE streaming.
/// Preserves real-time token-by-token streaming for reasoning, text, and tool-call deltas.
/// </summary>
public sealed class TornadoLLM(TornadoApi api, ChatModel model) : ILLM
{
    public async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var conv = api.Chat.CreateConversation();
        conv.Model = model;
        
        // Populate messages cleanly
        conv.AddMessage(messages.Select(m => m.ToTornadoMessage()));

        conv.RequestParameters.Tools = tools?.Select(t => t.ToTornadoTool()).ToList();
        conv.RequestParameters.ResponseFormat = responseSchema != null ? ChatRequestResponseFormats.StructuredJson("response", responseSchema.ToJsonElement()) : null;
        conv.RequestParameters.StreamOptions = ChatStreamOptions.KnownOptionsIncludeUsage;
        conv.RequestParameters.MaxTokens = 4096;

        var channel = Channel.CreateUnbounded<IMessageEvent>();

        _ = Task.Run(async () =>
        {
            try
            {
                int nextId = 0;
                int? textBlockId = null;
                int? reasoningBlockId = null;
                bool started = false;

                // Tool mapping for deltas
                var toolBlocks = new Dictionary<int, int>(); // LlmTornado Index -> AgentCore blockId

                async ValueTask EnsureStarted()
                {
                    if (!started)
                    {
                        started = true;
                        await channel.Writer.WriteAsync(new MessageStart(Role.Assistant), ct);
                    }
                }

                async ValueTask CloseActiveBlocks(bool closeReasoning = true, bool closeText = true)
                {
                    if (closeText && textBlockId != null)
                    {
                        await channel.Writer.WriteAsync(new TextEnd(textBlockId.Value), ct);
                        textBlockId = null;
                    }
                    if (closeReasoning && reasoningBlockId != null)
                    {
                        await channel.Writer.WriteAsync(new ReasoningEnd(reasoningBlockId.Value), ct);
                        reasoningBlockId = null;
                    }
                }

                var handler = new ChatStreamEventHandler
                {
                    MessageTypeResolvedHandler = async (role) => await EnsureStarted(),
                    ReasoningTokenHandler = async (data) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks(closeReasoning: false);
                        
                        if (reasoningBlockId == null)
                        {
                            reasoningBlockId = nextId++;
                            await channel.Writer.WriteAsync(new ReasoningStart(reasoningBlockId.Value), ct);
                        }
                        if (!string.IsNullOrEmpty(data.Content))
                        {
                            await channel.Writer.WriteAsync(new ReasoningDelta(reasoningBlockId.Value, data.Content), ct);
                        }
                    },
                    MessageTokenExHandler = async (tokenData) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks(closeText: false);
                        
                        if (textBlockId == null)
                        {
                            textBlockId = nextId++;
                            await channel.Writer.WriteAsync(new TextStart(textBlockId.Value), ct);
                        }
                        if (!string.IsNullOrEmpty(tokenData.Content))
                        {
                            await channel.Writer.WriteAsync(new TextDelta(textBlockId.Value, tokenData.Content), ct);
                        }
                    },
                    FunctionCallDeltaHandler = async (delta) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks();

                        int toolIndex = delta.Index ?? 0;
                        if (!toolBlocks.TryGetValue(toolIndex, out int blockId))
                        {
                            blockId = nextId++;
                            toolBlocks[toolIndex] = blockId;
                            
                            var callId = delta.CallId ?? $"call_{blockId}";
                            await channel.Writer.WriteAsync(new ToolCallStart(blockId, callId, delta.Name ?? ""), ct);
                        }

                        if (!string.IsNullOrEmpty(delta.ArgumentsDelta))
                        {
                            await channel.Writer.WriteAsync(new ToolCallDelta(blockId, delta.ArgumentsDelta), ct);
                        }

                        if (delta.IsComplete)
                        {
                            await channel.Writer.WriteAsync(new ToolCallEnd(blockId), ct);
                            toolBlocks.Remove(toolIndex);
                        }
                    },
                    OnFinished = async (data) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks();

                        foreach (var kvp in toolBlocks.OrderBy(x => x.Value))
                        {
                            await channel.Writer.WriteAsync(new ToolCallEnd(kvp.Value), ct);
                        }
                        toolBlocks.Clear();

                        if (data.Usage != null)
                        {
                            int inTokens = data.Usage.PromptTokens;
                            int outTokens = data.Usage.CompletionTokens;
                            int totalTokens = data.Usage.TotalTokens > 0 ? data.Usage.TotalTokens : inTokens + outTokens;
                            await channel.Writer.WriteAsync(new MetadataEvent(new TokenUsage(inTokens, outTokens, totalTokens)), ct);
                        }

                        await channel.Writer.WriteAsync(new MessageEnd(), ct);
                    }
                };

                await conv.StreamResponseRich(handler, ct);
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }
}
