using AgentCore.LLM.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AgentCore.Example;

internal class Program
{
    public static Action<ILLMOutput>? CurrentLlmEventHandler { get; set; }

    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "AgentCore Feature Showcase Example App";

        Console.WriteLine("AgentCore");
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
                        ?? "model";

        var baseUrlStr = Environment.GetEnvironmentVariable("LLM_BASE_URL")
                         ?? configuration["LLM:BaseUrl"]
                         ?? "http://127.0.0.1:1234";

        Uri? baseUrl = null;
        if (!string.IsNullOrWhiteSpace(baseUrlStr))
        {
            baseUrl = new Uri(baseUrlStr);
        }

        // 2. Setup Serilog Logging Provider
        Serilog.Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[Diag Log] [{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("agent_diagnostics.log", rollingInterval: Serilog.RollingInterval.Infinite, outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog();
        });

        // 3. Initialize the Chat Session
        var sessionsDir = Path.Combine(AppContext.BaseDirectory, "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            Directory.CreateDirectory(sessionsDir);
        }

        var chatConsole = new ChatConsole();

        var sessionFiles = GetSessionFiles();
        string sessionFile;
        var lastSessionFileLog = Path.Combine(sessionsDir, "last_session.txt");

        // Interactive startup session selection
        var startupSelected = chatConsole.SelectSessionMenu(sessionFiles, "");
        if (startupSelected == "NEW")
        {
            sessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        }
        else
        {
            sessionFile = startupSelected;
        }

        // Save last used session path
        File.WriteAllText(lastSessionFileLog, sessionFile);

        var session = await ChatSession.CreateAsync(
            apiKey,
            modelName,
            baseUrl,
            loggerFactory,
            sessionFile,
            evt => CurrentLlmEventHandler?.Invoke(evt));

        if (startupSelected != "NEW")
        {
            chatConsole.PrintNormalChat(session.Messages);
        }

        var appCts = new CancellationTokenSource();

        // 4. Console Interaction Loop
        while (!appCts.Token.IsCancellationRequested)
        {
            var input = chatConsole.ReadUserInput();
            if (input == null) break;

            // Empty line or single slash automatically opens the interactive TUI menu
            if (string.IsNullOrEmpty(input) || input == "/")
            {
                var choice = chatConsole.ShowInteractiveMenu();
                switch (choice)
                {
                    case "New session":
                        {
                            var newSessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                            await session.StartNewAsync(newSessionFile);
                            File.WriteAllText(lastSessionFileLog, newSessionFile);
                            Spectre.Console.AnsiConsole.Clear();
                        }
                        break;

                    case "Switch session":
                        {
                            var files = GetSessionFiles();
                            var selected = chatConsole.SelectSessionMenu(files, session.SessionFile);
                            if (selected != "NEW")
                            {
                                await session.LoadAsync(selected);
                                File.WriteAllText(lastSessionFileLog, selected);
                                Spectre.Console.AnsiConsole.Clear();
                                chatConsole.PrintNormalChat(session.Messages);
                            }
                            else
                            {
                                var newSessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                                await session.StartNewAsync(newSessionFile);
                                File.WriteAllText(lastSessionFileLog, newSessionFile);
                                Spectre.Console.AnsiConsole.Clear();
                            }
                        }
                        break;

                    case "Revert":
                        {
                            var targetIndex = chatConsole.PromptRevertMenu(session.Messages);
                            if (targetIndex.HasValue)
                            {
                                await session.RevertToAsync(targetIndex.Value);
                                Spectre.Console.AnsiConsole.Clear();
                                chatConsole.PrintNormalChat(session.Messages);
                            }
                        }
                        break;



                    case "Help":
                        chatConsole.PrintHelp();
                        break;

                    case "Exit":
                        appCts.Cancel();
                        break;
                }
                continue;
            }

            if (input.StartsWith("/"))
            {
                var parts = input.Split(' ', 2);
                var command = parts[0].ToLowerInvariant();
                var argument = parts.Length > 1 ? parts[1].Trim() : "";

                switch (command)
                {
                    case "/help":
                        chatConsole.PrintHelp();
                        break;

                    case "/menu":
                        {
                            var choice = chatConsole.ShowInteractiveMenu();
                            if (choice == "New session")
                            {
                                var newSessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                                await session.StartNewAsync(newSessionFile);
                                File.WriteAllText(lastSessionFileLog, newSessionFile);
                                Spectre.Console.AnsiConsole.Clear();
                            }
                            else if (choice == "Switch session")
                            {
                                var files = GetSessionFiles();
                                var selected = chatConsole.SelectSessionMenu(files, session.SessionFile);
                                if (selected != "NEW")
                                {
                                    await session.LoadAsync(selected);
                                    File.WriteAllText(lastSessionFileLog, selected);
                                    Spectre.Console.AnsiConsole.Clear();
                                    chatConsole.PrintNormalChat(session.Messages);
                                }
                                else
                                {
                                    var newSessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                                    await session.StartNewAsync(newSessionFile);
                                    File.WriteAllText(lastSessionFileLog, newSessionFile);
                                    Spectre.Console.AnsiConsole.Clear();
                                }
                            }
                            else if (choice == "Revert")
                            {
                                var targetIndex = chatConsole.PromptRevertMenu(session.Messages);
                                if (targetIndex.HasValue)
                                {
                                    await session.RevertToAsync(targetIndex.Value);
                                    Spectre.Console.AnsiConsole.Clear();
                                    chatConsole.PrintNormalChat(session.Messages);
                                }
                            }

                            else if (choice == "Help")
                            {
                                chatConsole.PrintHelp();
                            }
                            else if (choice == "Exit")
                            {
                                appCts.Cancel();
                            }
                        }
                        break;

                    case "/history":
                        chatConsole.PrintDiagnosticHistory(session.Messages);
                        break;



                    case "/revert":
                        {
                            var targetIndex = chatConsole.PromptRevertMenu(session.Messages);
                            if (targetIndex.HasValue)
                            {
                                await session.RevertToAsync(targetIndex.Value);
                                Spectre.Console.AnsiConsole.Clear();
                                chatConsole.PrintNormalChat(session.Messages);
                            }
                        }
                        break;

                    default:
                        Spectre.Console.AnsiConsole.MarkupLine($"[red]Unknown command: {command}. Type /help or /menu.[/]");
                        break;
                }
                continue;
            }

            using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(appCts.Token);
            try
            {
                var contentInput = new Text(input);
                var eventStream = session.Agent.InvokeStreamingAsync(contentInput, promptCts.Token);

                await chatConsole.RenderStreamAsync(eventStream, evt => { }, promptCts.Token);
                await session.RefreshAsync(promptCts.Token);
            }
            catch (OperationCanceledException)
            {
                Spectre.Console.AnsiConsole.MarkupLine("\n[yellow]Execution cancelled.[/]");
            }
            catch (Exception ex)
            {
                Spectre.Console.AnsiConsole.MarkupLine($"\n[red]Exception occurred: {Spectre.Console.Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    private static List<string> GetSessionFiles()
    {
        var sessionsDir = Path.Combine(AppContext.BaseDirectory, "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            return new List<string>();
        }
        return Directory.GetFiles(sessionsDir, "session_*.json")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => f.FullName)
            .ToList();
    }
}


