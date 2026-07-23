using AgentCore.LLM;
using AgentCore.LLM.Chat;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.Responses;
using LlmTornado.Responses.Events;
using System.Runtime.CompilerServices;

namespace AgentCore.LLM.Tornado;

public class TornadoLLM : ILLM
{
    private readonly TornadoApi _api;
    private readonly ChatModel _defaultModel;
    private readonly LLMOptions _options;
    private readonly LLMCapabilities _capabilities;

    public TornadoLLM(
        TornadoApi api, 
        string modelName,
        LLMCapabilities capabilities,
        LLMOptions? options = null)
    {
        _api = api;
        _defaultModel = new ChatModel(modelName);
        _capabilities = capabilities ?? new LLMCapabilities();
        _options = options ?? new LLMOptions();
    }

    public LLMCapabilities GetCapabilities() => _capabilities;

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<AgentCore.Tools.Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var activeModelName = options?.Model ?? _defaultModel.Name;
        var request = new ChatRequest
        {
            Model = new ChatModel(activeModelName),
        };

        var activeOptions = options ?? _options;
        request.ApplyOptions(activeOptions, tools);

        var chat = _api.Chat.CreateConversation(request);

        foreach (var msg in messages)
        {
            chat.AppendMessage(msg.ToTornadoMessage(activeModelName));
        }

        var channelOptions = new System.Threading.Channels.BoundedChannelOptions(1024)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
        };
        var channel = System.Threading.Channels.Channel.CreateBounded<LLMEvent>(channelOptions);

        var toolCallInfo = new System.Collections.Generic.Dictionary<int, (string Id, string Name)>();
        var streamedIndices = new System.Collections.Generic.HashSet<int>();

        var lastArguments = new System.Collections.Generic.Dictionary<int, string>();
        var assignedToolCallIds = new System.Collections.Generic.Dictionary<int, string>();

        _ = Task.Run(async () =>
        {
            try
            {
                await chat.StreamResponseRich(new ChatStreamEventHandler
                {
                    OnResponseEvent = async (evt) =>
                    {
                        if (evt is ResponseEventOutputItemAdded addedEvt && addedEvt.Item is ResponseFunctionToolCallItem fnCall)
                        {
                            toolCallInfo[addedEvt.OutputIndex] = (fnCall.CallId ?? fnCall.Id ?? "", fnCall.Name ?? "");
                        }
                        else if (evt is ResponseEventFunctionCallArgumentsDelta deltaEvt)
                        {
                            toolCallInfo.TryGetValue(deltaEvt.OutputIndex, out var info);
                            var toolCallDelta = new ToolCall(
                                info.Id ?? "",
                                info.Name ?? "",
                                System.Text.Json.Nodes.JsonValue.Create(deltaEvt.Delta),
                                deltaEvt.OutputIndex
                            );
                            streamedIndices.Add(deltaEvt.OutputIndex);
                            await channel.Writer.WriteAsync(toolCallDelta, ct);
                        }
                    },
                    ReasoningTokenHandler = async (reasoning) =>
                    {
                        if (reasoning.Content is not null)
                        {
                            await channel.Writer.WriteAsync(new AgentCore.LLM.Chat.Reasoning(reasoning.Content), ct);
                        }
                    },
                    MessagePartHandler = async (part) =>
                    {
                        if (part.Reasoning is not null && part.Reasoning.Content is not null)
                        {
                            await channel.Writer.WriteAsync(new AgentCore.LLM.Chat.Reasoning(part.Reasoning.Content), ct);
                        }
                        if (part.Text is not null)
                        {
                            await channel.Writer.WriteAsync(new Text(part.Text), ct);
                        }
                    },
                    FunctionCallHandler = async (calls) =>
                    {
                         int idx = 0;
                         foreach(var call in calls)
                         {
                              int currentIdx = idx++;
                              if (streamedIndices.Contains(currentIdx))
                              {
                                   continue;
                              }
                              var argsStr = call.Arguments ?? "";
                              lastArguments.TryGetValue(currentIdx, out var prevArgs);
                              prevArgs ??= "";

                              if (argsStr == prevArgs)
                              {
                                   continue;
                              }

                              if (argsStr.StartsWith(prevArgs))
                              {
                                   string deltaStr = argsStr.Substring(prevArgs.Length);
                                   lastArguments[currentIdx] = argsStr;

                                   string toolCallId;
                                   if (!string.IsNullOrEmpty(call.ToolCall?.Id))
                                   {
                                        toolCallId = call.ToolCall.Id;
                                   }
                                   else if (!assignedToolCallIds.TryGetValue(currentIdx, out toolCallId!))
                                   {
                                        toolCallId = Guid.NewGuid().ToString();
                                        assignedToolCallIds[currentIdx] = toolCallId;
                                   }

                                   await channel.Writer.WriteAsync(new ToolCall(
                                        toolCallId,
                                        call.Name,
                                        System.Text.Json.Nodes.JsonValue.Create(deltaStr),
                                        currentIdx
                                   ), ct);
                              }
                         }
                    },
                    OnUsageReceived = async (usage) => 
                    {
                        if (usage.TotalTokens > 0)
                        {
                            await channel.Writer.WriteAsync(new TokenUsage(usage.PromptTokens, usage.CompletionTokens), ct);
                        }
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex.TranslateException());
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        while (await channel.Reader.WaitToReadAsync(ct))
        {
            while (channel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }
}
