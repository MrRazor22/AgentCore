using AgentCore.LLM;
using AgentCore.LLM.Chat;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using System.Runtime.CompilerServices;

namespace AgentCore.LLM.Tornado;

public class TornadoLLMProvider : ILLM
{
    private readonly TornadoApi _api;
    private readonly ChatModel _defaultModel;
    private readonly LLMOptions _options;
    private readonly LLMCapabilities _capabilities;

    public TornadoLLMProvider(
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
            chat.AppendMessage(msg.ToTornadoMessage());
        }

        var channelOptions = new System.Threading.Channels.BoundedChannelOptions(1024)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
        };
        var channel = System.Threading.Channels.Channel.CreateBounded<LLMEvent>(channelOptions);

        _ = Task.Run(async () =>
        {
            try
            {
                await chat.StreamResponseRich(new ChatStreamEventHandler
                {
                    ReasoningTokenHandler = async (reasoning) =>
                    {
                        if (reasoning.Content is not null)
                        {
                            await channel.Writer.WriteAsync(new Reasoning(reasoning.Content), ct);
                        }
                    },
                    MessagePartHandler = async (part) =>
                    {
                        if (part.Reasoning is not null && part.Reasoning.Content is not null)
                        {
                            await channel.Writer.WriteAsync(new Reasoning(part.Reasoning.Content), ct);
                        }
                        if (part.Text is not null)
                        {
                            await channel.Writer.WriteAsync(new Text(part.Text), ct);
                        }
                    },
                    FunctionCallHandler = async (calls) =>
                    {
                         // Emit tool calls
                         int idx = 0;
                         foreach(var call in calls)
                         {
                              var argsStr = call.Arguments;
                              await channel.Writer.WriteAsync(new ToolCall(call.ToolCall?.Id ?? Guid.NewGuid().ToString(), call.Name, System.Text.Json.Nodes.JsonValue.Create(argsStr), idx++), ct);
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
