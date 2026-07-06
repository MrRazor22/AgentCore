using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AgentCore;
using AgentCore.Diagnostics;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tooling;
using TestApp.Decorators;
using TestApp.Services;
using TestApp.BuiltInTools;

namespace TestApp.Endpoints;

public static class ChatEndpoints
{
    public record ChatRequest(string Message);

    public static void MapChatEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/sessions/{id}/message", async (
            string id,
            ChatRequest req,
            HttpContext httpContext,
            IAgentEventBus eventBus,
            IApprovalService approvalService,
            ILoggerFactory loggerFactory) =>
        {
            var response = httpContext.Response;
            response.ContentType = "text/event-stream";
            response.Headers.Append("Cache-Control", "no-cache");
            response.Headers.Append("Connection", "keep-alive");

            var ct = httpContext.RequestAborted;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Build subscription pipeline
            var subscriptionTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in eventBus.Subscribe(id, cts.Token))
                    {
                        var json = JsonSerializer.Serialize(evt, evt.GetType(), new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                        await response.WriteAsync($"data: {json}\n\n", cts.Token);
                        await response.Body.FlushAsync(cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            }, cts.Token);

            // Signal message started
            eventBus.Publish(new MessageStartedEvent { SessionId = id });

            try
            {
                // Setup provider matching test configuration
                var apiKey = "lmstudio";
                var model = "qwen/qwen3.5-9b";
                var baseUrl = new Uri("http://127.0.0.1:1234");
                var provider = AgentCore.Providers.Tornado.TornadoProvider.CreateLLMProvider(apiKey, model, baseUrl);

                var registry = new ToolRegistry(loggerFactory.CreateLogger<ToolRegistry>());
                registry.RegisterAll<AgentCore.BuiltInTools.MathTools>();
                registry.RegisterAll<AgentCore.LLM.BuiltInTools.SearchTools>();
                registry.RegisterAll<AgentCore.LLM.BuiltInTools.WeatherTool>();
                registry.RegisterAll<AgentCore.LLM.BuiltInTools.GeoTools>();
                registry.RegisterAll<AgentCore.LLM.BuiltInTools.ConversionTools>();
                registry.RegisterAll<CommandLineTools>();

                var agent = LLMAgent.Create("chat")
                    .WithProvider(provider, new() { ContextWindow = 8000 })
                    .WithTools(r =>
                    {
                        foreach (var tool in registry.Tools) r.Register(tool);
                    })
                    .WithLoggerFactory(loggerFactory)
                    .AddMemoryLayer(inner => new DurableFileMemory(new ObservabilityMemoryDecorator(inner, eventBus, id), id))
                    .AddToolExecutorLayer(inner => new ApprovalToolExecutor(new ObservabilityToolExecutorDecorator(inner, eventBus, id), registry, approvalService, id))
                    .AddLlmExecutorLayer(inner => new ObservabilityLlmExecutorDecorator(inner, eventBus, id))
                    .AddDiagnostics()
                    .Build();

                var tracer = httpContext.RequestServices.GetRequiredService<IAgentTracer>();
                var diagnosticAgent = new DiagnosticAgent(agent, tracer, id);

                await foreach (var agentEvt in diagnosticAgent.InvokeStreamingAsync(new Text(req.Message), ct))
                {
                    switch (agentEvt)
                    {
                        case TextEvent t:
                            eventBus.Publish(new TextDeltaEvent(t.Delta) { SessionId = id });
                            break;
                        case ReasoningEvent r:
                            eventBus.Publish(new ReasoningDeltaEvent(r.Delta) { SessionId = id });
                            break;
                        case ToolCallEvent tc:
                            eventBus.Publish(new ToolCallStartedEvent(tc.Call.Id, tc.Call.Name, tc.Call.Arguments.ToString()) { SessionId = id });
                            break;
                        case AgentToolResultEvent tr:
                            eventBus.Publish(new ToolResultEvent(tr.Result.CallId, tr.Result.CallId, tr.Result.Result?.ForLlm() ?? "") { SessionId = id });
                            break;
                        case AgentErrorEvent err:
                            eventBus.Publish(new ErrorEvent(err.Error.Message) { SessionId = id });
                            break;
                    }
                }

                eventBus.Publish(new CompletedEvent { SessionId = id });
            }
            catch (OperationCanceledException)
            {
                eventBus.Publish(new ErrorEvent("Generation stopped by user.") { SessionId = id });
            }
            catch (Exception ex)
            {
                eventBus.Publish(new ErrorEvent(ex.Message) { SessionId = id });
            }
            finally
            {
                // Give SSE subscriber a split second to drain, then cancel and await
                await Task.Delay(100, CancellationToken.None);
                cts.Cancel();
                try { await subscriptionTask; } catch { }
            }
        });
    }
}
