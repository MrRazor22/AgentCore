using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using AgentCore;
using Spectre.Console;
using CodeSharp.UI;

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
            var baseUrl = config.BaseUrl;
            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
            var openAIClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), clientOptions);
            var chatClient = openAIClient.GetChatClient(config.Model).AsIChatClient();

            var streamingLayer = new StreamingLLMLayer();
            IAgent agent = Agent.Create()
                .WithMEAI(chatClient)
                .AddLLMLayer(streamingLayer)
                .WithInstructions("You are CodeSharp, a helpful, precise, and concise AI assistant.")
                .Build();

            var chatUi = new ChatUI(agent, config.Model, workspacePath, streamingLayer);
            await chatUi.RunAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error building agent:[/] {ex.Message}");
        }
    }
}
