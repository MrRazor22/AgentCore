using AgentCore.Conversation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace AgentCore.MCP.Server;

public sealed class AgentMcpServerOptions
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public McpTransportType Transport { get; set; } = McpTransportType.Stdio;
    public string? HttpEndpoint { get; set; }
}

public enum McpTransportType
{
    Stdio,
    Http
}

public sealed class AgentMcpServer
{
    private readonly IAgent _agent;
    private readonly AgentMcpServerOptions _options;

    public AgentMcpServer(IAgent agent, AgentMcpServerOptions options)
    {
        _agent = agent;
        _options = options;
    }

    public static Task RunAsync(IAgent agent, AgentMcpServerOptions options, CancellationToken ct = default)
    {
        var server = new AgentMcpServer(agent, options);
        return server.RunInternalAsync(ct);
    }

    private ModelContextProtocol.Protocol.Tool BuildAgentTool() =>
        new()
        {
            Name = _options.Name,
            Description = _options.Description ?? "AI Agent",
            InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "input":      { "type": "string", "description": "The task or question for the agent" },
                        "session_id": { "type": "string", "description": "Optional session ID for conversation continuity" }
                    },
                    "required": ["input"]
                }
                """).RootElement
        };

    private async ValueTask<CallToolResult> HandleCallToolAsync(
        ModelContextProtocol.Server.RequestContext<CallToolRequestParams> requestContext,
        CancellationToken ct)
    {
        var p = requestContext.Params
            ?? throw new McpException($"Missing parameters for tool '{_options.Name}'.");

        if (!string.Equals(p.Name, _options.Name, StringComparison.Ordinal))
            throw new McpException($"Tool '{p.Name}' not found. This server exposes '{_options.Name}'.");

        var args = p.Arguments ?? new Dictionary<string, JsonElement>();

        string input = args.TryGetValue("input", out var inp) && inp.ValueKind == JsonValueKind.String
            ? inp.GetString() ?? ""
            : "";

        string? sessionId = args.TryGetValue("session_id", out var sid) && sid.ValueKind == JsonValueKind.String
            ? sid.GetString()
            : null;

        var response = await _agent.InvokeAsync(new Text(input), sessionId, ct);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };
    }

    private async Task RunInternalAsync(CancellationToken ct)
    {
        if (_options.Transport == McpTransportType.Stdio)
        {
            var builder = Host.CreateApplicationBuilder();

            // Redirect logs to stderr so stdout is clean for MCP stdio transport
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(c => c.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services
                .AddMcpServer(opts => opts.ServerInfo = new Implementation { Name = _options.Name, Version = "1.0.0" })
                .WithStdioServerTransport()
                .WithListToolsHandler(async (req, innerCt) =>
                {
                    await Task.CompletedTask;
                    return new ListToolsResult { Tools = [BuildAgentTool()] };
                })
                .WithCallToolHandler(HandleCallToolAsync);

            using var host = builder.Build();
            await host.RunAsync(ct);
        }
        else
        {
            var builder = WebApplication.CreateBuilder();

            builder.Services
                .AddMcpServer(opts => opts.ServerInfo = new Implementation { Name = _options.Name, Version = "1.0.0" })
                .WithHttpTransport()
                .WithListToolsHandler(async (req, innerCt) =>
                {
                    await Task.CompletedTask;
                    return new ListToolsResult { Tools = [BuildAgentTool()] };
                })
                .WithCallToolHandler(HandleCallToolAsync);

            var app = builder.Build();
            app.MapMcp();

            if (!string.IsNullOrWhiteSpace(_options.HttpEndpoint))
                app.Urls.Add(_options.HttpEndpoint);

            await app.RunAsync(ct);
        }
    }
}

public static class AgentMcpServerExtensions
{
    public static AgentMcpServer AsMcpServer(this IAgent agent, AgentMcpServerOptions options)
        => new(agent, options);
}
