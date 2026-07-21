using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentCore.Example;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "AgentCore Feature Showcase Example App";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================================");
        Console.WriteLine("                AGENTCORE FEATURE SHOWCASE DEMO                  ");
        Console.WriteLine("=================================================================");
        Console.ResetColor();
        Console.WriteLine("This example application showcases the power of the AgentCore framework.");
        Console.WriteLine("Features demonstrated: ReAct workflow, streaming, tools, persistent memory layer, and reversion.");
        Console.WriteLine();

        // 1. Resolve LLM Configuration
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY") 
                     ?? configuration["LLM:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Enter LLM API Key (Default: lm-studio): ");
            Console.ResetColor();
            apiKey = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "lm-studio";
            }
        }

        var modelName = Environment.GetEnvironmentVariable("LLM_MODEL") 
                        ?? configuration["LLM:Model"] 
                        ?? "qwen/qwen3.5-9b";

        var baseUrlStr = Environment.GetEnvironmentVariable("LLM_BASE_URL") 
                         ?? configuration["LLM:BaseUrl"] 
                         ?? "http://127.0.0.1:1234";

        Uri? baseUrl = null;
        if (!string.IsNullOrWhiteSpace(baseUrlStr))
        {
            baseUrl = new Uri(baseUrlStr);
        }

        // 2. Setup Toggle Logging Provider
        var toggleLogger = new ToggleLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(toggleLogger);
        });

        // 3. Initialize the Chat Session
        var sessionFile = "active_session.json";
        var session = await ChatSession.CreateAsync(apiKey, modelName, baseUrl, loggerFactory, sessionFile);

        PrintHelp();

        var ctSource = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            ctSource.Cancel();
        };

        // 4. Console Interaction Loop
        while (!ctSource.Token.IsCancellationRequested)
        {
            Console.Write("\nUser > ");
            var input = Console.ReadLine();
            if (input == null) break;
            input = input.Trim();

            if (string.IsNullOrEmpty(input)) continue;

            if (input.StartsWith("/"))
            {
                await HandleCommandAsync(input, session, toggleLogger);
                continue;
            }

            try
            {
                var contentInput = new Text(input);
                await foreach (var evt in session.Agent.InvokeStreamingAsync(contentInput, ctSource.Token))
                {
                    if (evt is ToolCallEvent toolCallEvent)
                    {
                        var toolCall = toolCallEvent.ToolCall;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n[Agent calling tool: {toolCall.Name}({toolCall.ArgumentsObject})]");
                        Console.ResetColor();
                    }
                    else if (evt is ToolResultEvent toolResult)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[Tool Result: {toolResult.Result.Result?.ForLlm()}]");
                        Console.ResetColor();
                    }
                    else if (evt is AgentResponseEvent<string> response)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("Assistant > ");
                        Console.ResetColor();
                        Console.WriteLine(response.Response);
                    }
                    else if (evt is ErrorEvent error)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n[Error: {error.Error.Message}]");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nException occurred: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private static async Task HandleCommandAsync(string input, ChatSession session, ToggleLoggerProvider loggerProvider)
    {
        var parts = input.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : "";

        switch (command)
        {
            case "/help":
                PrintHelp();
                break;

            case "/new":
                await session.StartNewAsync();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("New conversation session started. Memory cleared.");
                Console.ResetColor();
                break;

            case "/logs":
                loggerProvider.Enabled = !loggerProvider.Enabled;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Framework logging toggled: {(loggerProvider.Enabled ? "ENABLED" : "DISABLED")}");
                Console.ResetColor();
                break;

            case "/history":
                PrintHistory(session);
                break;

            case "/revert":
                if (int.TryParse(argument, out var index))
                {
                    var localHistory = session.Messages;
                    if (index >= 0 && index < localHistory.Count)
                    {
                        await session.RevertToAsync(index);
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"Reverted conversation history back to message index {index}. Truncating subsequent messages.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Error: Index must be between 0 and {localHistory.Count - 1}.");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Please specify a valid integer index (e.g. /revert 3).");
                    Console.ResetColor();
                }
                break;

            case "/save":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Specify a filename (e.g., /save chat1.json).");
                    Console.ResetColor();
                    return;
                }
                try
                {
                    session.Save(argument);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Session history saved successfully to '{argument}'.");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error saving session: {ex.Message}");
                    Console.ResetColor();
                }
                break;

            case "/load":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Specify a filename to load (e.g., /load chat1.json).");
                    Console.ResetColor();
                    return;
                }
                try
                {
                    await session.LoadAsync(argument);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Session history loaded successfully from '{argument}'.");
                    Console.ResetColor();
                    PrintHistory(session, limit: 5);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error loading session: {ex.Message}");
                    Console.ResetColor();
                }
                break;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unknown slash command: {command}. Type /help to see all commands.");
                Console.ResetColor();
                break;
        }
    }

    private static void PrintHistory(ChatSession session, int limit = int.MaxValue)
    {
        var history = session.Messages;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n--- Conversation Message History ---");
        if (history.Count == 0)
        {
            Console.WriteLine("(Empty)");
        }
        else
        {
            int start = Math.Max(0, history.Count - limit);
            if (start > 0)
            {
                Console.WriteLine($"... [restored {start} older messages] ...");
            }
            for (int i = start; i < history.Count; i++)
            {
                var msg = history[i];
                var summary = string.Join(" ", msg.Contents.Select(c => c.ForLlm()));
                if (summary.Length > 80) summary = summary[..77] + "...";
                Console.WriteLine($"[{i}] {msg.Role}: {summary}");
            }
        }
        Console.WriteLine("------------------------------------\n");
        Console.ResetColor();
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n--- Available Commands ---");
        Console.WriteLine("  /new             - Start a new conversation (clear session memory)");
        Console.WriteLine("  /history         - List all messages in current session with indices");
        Console.WriteLine("  /revert <index>  - Truncate conversation history back to specific message index");
        Console.WriteLine("  /save <file>     - Save current session history to a JSON file");
        Console.WriteLine("  /load <file>     - Load session history from a JSON file");
        Console.WriteLine("  /logs            - Toggle verbose framework logging");
        Console.WriteLine("  /help            - Show this help menu");
        Console.WriteLine("--------------------------\n");
        Console.ResetColor();
    }
}

public class ToggleLoggerProvider : ILoggerProvider
{
    public bool Enabled { get; set; } = false;

    public ILogger CreateLogger(string categoryName) => new ToggleLogger(categoryName, this);

    public void Dispose() { }

    private class ToggleLogger : ILogger
    {
        private readonly string _category;
        private readonly ToggleLoggerProvider _provider;

        public ToggleLogger(string category, ToggleLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.Enabled && logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[Diag Log] [{logLevel}] [{_category}] {formatter(state, exception)}");
            if (exception != null)
            {
                Console.WriteLine(exception.ToString());
            }
            Console.ResetColor();
        }
    }
}
