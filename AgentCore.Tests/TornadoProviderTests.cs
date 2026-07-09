using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LlmTornado;
using LlmTornado.Chat.Models;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Providers.Tornado;
using AgentCore.LLM.Exceptions;

namespace AgentCore.Tests;

public class TornadoProviderTests
{
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task TornadoLLMProvider_StreamAsync_MapsTransientNetworkFailureToRetryableException()
    {
        // Arrange
        // Using a port that is likely closed/refused to trigger immediate SocketException/HttpRequestException
        int port = GetFreePort();
        var api = new TornadoApi(new Uri($"http://127.0.0.1:{port}/v1/"), "api-key");
        var provider = new TornadoLLMProvider(api, new ChatModel("gpt-4o"));

        // Act & Assert
        await Assert.ThrowsAsync<RetryableException>(async () =>
        {
            await foreach (var delta in provider.StreamAsync(new[] { new Message(Role.User, new Text("Hello")) }, new LLMOptions()))
            {
                // Consume stream
            }
        });
    }

    [Fact]
    public async Task TornadoLLMProvider_StreamAsync_MapsContextLengthExceededResponse()
    {
        // Arrange
        int port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var api = new TornadoApi(new Uri($"http://127.0.0.1:{port}/"), "api-key");
        var provider = new TornadoLLMProvider(api, new ChatModel("gpt-4o"));

        // Run background HTTP responder to return a context length exceeded error
        var respondTask = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 400;
                    string errorResponse = "{\"error\": {\"message\": \"context_length_exceeded: token limit exceeded\", \"type\": \"invalid_request_error\"}}";
                    byte[] buffer = Encoding.UTF8.GetBytes(errorResponse);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    context.Response.Close();
                }
            }
            catch
            {
                // Ignore listener exceptions when stopped
            }
        });

        // Act & Assert
        await Assert.ThrowsAsync<ContextLengthExceededException>(async () =>
        {
            await foreach (var delta in provider.StreamAsync(new[] { new Message(Role.User, new Text("Hello")) }, new LLMOptions()))
            {
                // Consume stream
            }
        });

        listener.Close();
        try
        {
            await respondTask;
        }
        catch
        {
            // Ignore any thread exceptions on disposal
        }
    }
}
