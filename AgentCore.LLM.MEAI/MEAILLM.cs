using AgentCore.LLM.Chat;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentCore.LLM.MEAI;

/// <summary>
/// Adapts a Microsoft.Extensions.AI IChatClient to the AgentCore ILLM interface.
/// </summary>
public class MEAILLM : ILLM
{
    private readonly IChatClient _client;
    private readonly LLMCapabilities _capabilities;

    public MEAILLM(IChatClient client, LLMCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _capabilities = capabilities ?? new LLMCapabilities();
    }

    public LLMCapabilities GetCapabilities() => _capabilities;

    public async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<AgentCore.Tools.Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatMessages = messages.Select(m => m.ToMEAIMessage()).ToList();

        var chatOptions = new ChatOptions();
        if (options != null)
        {
            if (options.Model != null)
            {
                chatOptions.ModelId = options.Model;
            }
            if (options.Temperature.HasValue)
            {
                chatOptions.Temperature = options.Temperature.Value;
            }
            if (options.MaxOutputTokens.HasValue)
            {
                chatOptions.MaxOutputTokens = options.MaxOutputTokens.Value;
            }
            if (options.ResponseSchema != null)
            {
                try
                {
                    var schemaJson = options.ResponseSchema.ToString();
                    var jsonElement = JsonSerializer.Deserialize<JsonElement>(schemaJson);
                    chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(jsonElement);
                }
                catch
                {
                    chatOptions.ResponseFormat = ChatResponseFormat.Json;
                }
            }
        }

        if (tools is { Count: > 0 })
        {
            chatOptions.Tools = tools.Select(t => (AITool)new AgentCoreAIFunction(t)).ToList();
        }

        int inputTokens = 0;
        int outputTokens = 0;
        int? reasoningTokens = null;

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, chatOptions, ct).ConfigureAwait(false))
        {
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        yield return new TextDelta(textContent.Text);
                    }
                    else if (content is TextReasoningContent reasoningContent && !string.IsNullOrEmpty(reasoningContent.Text))
                    {
                        yield return new ReasoningDelta(reasoningContent.Text);
                    }
                    else if (content is FunctionCallContent fnCall)
                    {
                        string argsStr = "";
                        if (fnCall.Arguments != null)
                        {
                            try
                            {
                                argsStr = JsonSerializer.Serialize(fnCall.Arguments);
                            }
                            catch { }
                        }
                        yield return new ToolCallDelta(fnCall.CallId ?? "", fnCall.Name, argsStr);
                    }
                    else if (content is UsageContent usageContent)
                    {
                        var usage = usageContent.Details;
                        inputTokens += (int)(usage.InputTokenCount ?? 0);
                        outputTokens += (int)(usage.OutputTokenCount ?? 0);
                        if (usage.ReasoningTokenCount.HasValue)
                        {
                            reasoningTokens = (reasoningTokens ?? 0) + (int)usage.ReasoningTokenCount.Value;
                        }
                    }
                }
            }

            if (update.RawRepresentation is not null)
            {
                List<ToolCallDelta>? rawDeltas = null;
                try
                {
                    dynamic rawUpdate = update.RawRepresentation;
                    var toolCallUpdates = rawUpdate.ToolCallUpdates;
                    if (toolCallUpdates != null)
                    {
                        rawDeltas = new List<ToolCallDelta>();
                        foreach (dynamic toolCallUpdate in toolCallUpdates)
                        {
                            string? callId = toolCallUpdate.ToolCallId;
                            string? funcName = toolCallUpdate.FunctionName;
                            string? argDelta = toolCallUpdate.FunctionArgumentsUpdate?.ToString();
                            int? index = null;
                            try
                            {
                                index = (int?)toolCallUpdate.Index;
                            }
                            catch { }

                            rawDeltas.Add(new ToolCallDelta(callId ?? "", funcName, argDelta, index));
                        }
                    }
                }
                catch { }

                if (rawDeltas != null)
                {
                    foreach (var d in rawDeltas)
                    {
                        yield return d;
                    }
                }
            }

            if (update.FinishReason is { } finishReason)
            {
                yield return new Metadata(
                    InputTokens: inputTokens,
                    OutputTokens: outputTokens,
                    ReasoningTokens: reasoningTokens,
                    FinishReason: finishReason.Value
                );
            }
        }
    }
}
