using AgentCore.Chat;
using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLMCore
{
    public static class LLMClientExtensions
    {
        // STRUCTURED
        public static Task<T> GetStructuredAsync<T>(
            this ILLMClient client,
            string userMessage,
            IEnumerable<Tool>? allowedTools = null,
            ToolCallMode toolMode = ToolCallMode.Disabled,
            string model = null,
            LLMGenerationOptions options = null,
            Action<string>? onStream = null,
            CancellationToken ct = default)
        {
            var convo = new Conversation().AddUser(userMessage);
            return client.GetStructuredAsync<T>(
                convo,
                allowedTools,
                toolMode,
                model,
                options,
                onStream,
                ct
            );
        }
        public static async Task<T> GetStructuredAsync<T>(
            this ILLMClient client,
            Conversation prompt,
            IEnumerable<Tool>? allowedTools = null,
            ToolCallMode toolMode = ToolCallMode.Disabled,
            string model = null,
            LLMGenerationOptions options = null,
            Action<string>? onStream = null,
            CancellationToken ct = default)
        {
            var req = new LLMStructuredRequest(prompt: prompt,
                                               resultType: typeof(T),
                                               allowedTools: allowedTools,
                                               toolCallMode: toolMode,
                                               model: model,
                                               options: options);

            var resp = await client.ExecuteAsync<T>(req, ct, onStream: chunk =>
            {
                if (chunk.Kind == StreamKind.Text && !string.IsNullOrWhiteSpace(chunk.AsText()))
                    onStream?.Invoke(chunk.AsText()!);
            });
            return resp.Result;
        }

        // TEXT
        public static async Task<LLMResponse> GetResponseAsync(
            this ILLMClient client,
            Conversation prompt,
            string model = null,
            LLMGenerationOptions options = null,
            CancellationToken ct = default,
            Action<string> onStream = null)
        {
            // Plain text = NO tools.
            var req = new LLMRequest(prompt: prompt,
                                     allowedTools: null,
                                     toolCallMode: ToolCallMode.Disabled,
                                     model: model,
                                     options: options);

            return await client.ExecuteAsync(req, ct, onStream: chunk =>
            {
                if (chunk.Kind == StreamKind.Text && !string.IsNullOrWhiteSpace(chunk.AsText()))
                    onStream?.Invoke(chunk.AsText()!);
            });
        }

        //Tool call
        public static async Task<LLMResponse> GetResponseAsync(
            this ILLMClient client,
            Conversation convo,
            ToolCallMode toolMode = ToolCallMode.Auto,
            string? model = null,
            LLMGenerationOptions? options = null,
            CancellationToken ct = default,
            Action<string>? onStream = null,
            params Tool[] tools)   // optional
        {
            var req = new LLMRequest(convo, toolMode, tools, model, options);

            return await client.ExecuteAsync(req, ct, onStream: chunk =>
            {
                if (chunk.Kind == StreamKind.Text && !string.IsNullOrWhiteSpace(chunk.AsText()))
                    onStream?.Invoke(chunk.AsText()!);
            });
        }
    }
}
