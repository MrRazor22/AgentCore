using AgentCore.LLM;
using AgentCore.LLM.Chat;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using System.Runtime.CompilerServices;

namespace AgentCore.Providers.Tornado;

public class TornadoLLMProvider : ILLMProvider
{
    private readonly TornadoApi _api;
    private readonly ChatModel _defaultModel;
    private readonly LLMOptions _options;

    private readonly Dictionary<string, LLMMetadata> _models = new(StringComparer.OrdinalIgnoreCase);

    public TornadoLLMProvider(
        TornadoApi api, 
        IReadOnlyList<LLMMetadata> models,
        LLMOptions? options = null)
    {
        if (models == null || models.Count == 0)
        {
            throw new ArgumentException("At least one model must be provided to the LLM provider configuration.", nameof(models));
        }

        _api = api;
        _options = options ?? new LLMOptions();

        foreach (var m in models)
        {
            _models[m.Id] = m;
        }

        var defaultMeta = models[0];
        _defaultModel = new ChatModel(defaultMeta.Id);
    }

    public async Task<LLMMetadata> GetModelInfoAsync(string? modelName = null, CancellationToken ct = default)
    {
        var name = modelName ?? _defaultModel.Name;

        if (_models.TryGetValue(name, out var metadata))
        {
            return metadata;
        }

        throw new InvalidOperationException($"LLMMetadata for model '{name}' could not be resolved. Please supply it in the models collection passed to the provider constructor.");
    }

    public async IAsyncEnumerable<IContentDelta> StreamAsync(
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
        var channel = System.Threading.Channels.Channel.CreateBounded<IContentDelta>(channelOptions);

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
                            await channel.Writer.WriteAsync(new ReasoningDelta(reasoning.Content), ct);
                        }
                    },
                    MessagePartHandler = async (part) =>
                    {
                        if (part.Reasoning is not null && part.Reasoning.Content is not null)
                        {
                            await channel.Writer.WriteAsync(new ReasoningDelta(part.Reasoning.Content), ct);
                        }
                        if (part.Text is not null)
                        {
                            await channel.Writer.WriteAsync(new TextDelta(part.Text), ct);
                        }
                    },
                    FunctionCallHandler = async (calls) =>
                    {
                         // Emit tool calls
                         int idx = 0;
                         foreach(var call in calls)
                         {
                              var argsStr = call.Arguments;
                              await channel.Writer.WriteAsync(new ToolCallDelta(idx++, call.ToolCall?.Id ?? Guid.NewGuid().ToString(), call.Name, argsStr), ct);
                         }
                    },
                    OnUsageReceived = async (usage) => 
                    {
                        if (usage.TotalTokens > 0)
                        {
                            await channel.Writer.WriteAsync(new MetaDelta(null, InputTokens: usage.PromptTokens, OutputTokens: usage.CompletionTokens, Model: activeModelName), ct);
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

