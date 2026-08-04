using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using AgentCore;
using AgentCore.Tools;
using Spectre.Console;
using CodeSharp.UI;
using CodeSharp.Layers;
using Serilog;
using Microsoft.Extensions.Logging;

namespace CodeSharp;

internal class App
{
    private class Config
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    private static async Task Main(string[] args)
    {
        // Set console output encoding to UTF8 for proper rendering of unicode symbols
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 1. Locate config.json
        string configPath = "config.json";
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        }
        if (!File.Exists(configPath))
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "config.json");
                if (File.Exists(candidate))
                {
                    configPath = candidate;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine("[bold red]Error:[/] Could not find [yellow]config.json[/] in any parent directories.");
            return;
        }

        // 2. Read config
        Config? config;
        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            config = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error reading config.json:[/] {ex.Message}");
            return;
        }

        if (config == null || string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Model))
        {
            AnsiConsole.MarkupLine("[bold red]Error:[/] Invalid configuration in [yellow]config.json[/]. Please check BaseUrl and Model.");
            return;
        }

        // Determine actual workspace path (upwards traversal to find .git or .sln)
        string workspacePath = Directory.GetCurrentDirectory();
        var searchDir = AppContext.BaseDirectory;
        while (searchDir != null)
        {
            if (Directory.Exists(Path.Combine(searchDir, ".git")) || File.Exists(Path.Combine(searchDir, "AgentCore.sln")))
            {
                workspacePath = searchDir;
                break;
            }
            searchDir = Path.GetDirectoryName(searchDir);
        }

        // 3. Build Agent and Run UI
        try
        {
            // 3. Configure Serilog rolling file logger
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "agentcore-.log"),
                    rollingInterval: Serilog.RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            var lf = new Microsoft.Extensions.Logging.LoggerFactory()
                .AddSerilog();

            var baseUrl = config.BaseUrl;
            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
            var openAIClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), clientOptions);
            var chatClient = openAIClient.GetChatClient(config.Model).AsIChatClient();

            var streamingLayer = new StreamingLLMLayer();

            // Initialize static tool contexts with workspace boundary
            CodeSharp.Tools.FileTools.Initialize(workspacePath);
            CodeSharp.Tools.SearchTool.Initialize(workspacePath);

            // Instantiate stateful/instance tools
            var shellTool = new CodeSharp.Tools.ShellTool(workspacePath);
            var webTools = new CodeSharp.Tools.WebTools();
            var todoTool = new CodeSharp.Tools.TodoTool();
            var scheduleTool = new CodeSharp.Tools.ScheduleTool();

            // Define data-driven tool permissions
            var permissions = new Dictionary<string, ToolPermission>
            {
                ["ReadFile"]   = ToolPermission.Allow,
                ["Search"]     = ToolPermission.Allow,
                ["SearchWeb"]  = ToolPermission.Allow,
                ["TodoList"]   = ToolPermission.Allow,
                ["Schedule"]   = ToolPermission.Allow,
                ["EditFile"]   = ToolPermission.Confirm,
                ["Filesystem"] = ToolPermission.Confirm,
                ["RunCommand"] = ToolPermission.Confirm,
            };

            // Defense-in-depth guardrails
            var guardrails = DenyRules.Combine(
                DenyRules.CommandPatterns(
                    "rm -rf /", "format c:", "del /s /q c:\\",
                    ":(){:|:&};:", "mkfs.", "dd if="
                )
            );

            var formatter = new CodeSharp.UI.CompositeToolDisplayFormatter(new CodeSharp.UI.IToolDisplayFormatter[]
            {
                new CodeSharp.UI.RunCommandFormatter(),
                new CodeSharp.UI.EditFileFormatter(),
                new CodeSharp.UI.FilesystemFormatter(),
                new CodeSharp.UI.SearchToolFormatter(),
                new CodeSharp.UI.SearchWebFormatter()
            });

            var approvalLayer = new CodeSharp.Layers.ApprovalLayer(
                permissions,
                ExecutionPolicy.Strict,
                new CodeSharp.UI.ConsoleApprovalPrompt(formatter),
                guardrails
            );

            IAgent agent = Agent.Create()
                .WithLoggerFactory(lf)
                .WithMEAI(chatClient)
                .AddLLMLayer(streamingLayer)
                .AddLLMLayer(new CodeSharp.Layers.StreamingToolCallParserLayer())
                .WithTools(shellTool)
                .WithTools(typeof(CodeSharp.Tools.FileTools))
                .WithTools(typeof(CodeSharp.Tools.SearchTool))
                .WithTools(webTools)
                .WithTools(todoTool)
                .WithTools(scheduleTool)
                .AddToolingLayer(approvalLayer)
                .WithInstructions(
                    "You are CodeSharp, an expert agentic AI coding assistant.\n" +
                    "Keep your responses precise, direct, and to the point. Do not add needless filler, conversational bloat, or generic pleasantries.\n" +
                    "Use workspace-relative paths for file tools. Do not invent path schemes or prefixes.\n" +
                    "Prefer Search for directory listing, file discovery, filename matching, and repository content search. Use ReadFile to inspect file contents. Do not use RunCommand as a substitute for Search or ReadFile. Use RunCommand when shell execution is inherently required, such as builds, tests, git operations, package managers, scripts, or application execution."
                )
                .Build();

            var chatUi = new ChatUI(agent, config.Model, workspacePath, streamingLayer, formatter);
            await chatUi.RunAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error building agent:[/] {ex.Message}");
        }
    }
}
