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
        var sessionsDir = Path.Combine(AppContext.BaseDirectory, "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            Directory.CreateDirectory(sessionsDir);
        }

        string sessionFile;
        var lastSessionFileLog = Path.Combine(sessionsDir, "last_session.txt");
        if (File.Exists(lastSessionFileLog))
        {
            var savedPath = File.ReadAllText(lastSessionFileLog).Trim();
            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
            {
                sessionFile = savedPath;
            }
            else
            {
                var files = Directory.GetFiles(sessionsDir, "session_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();
                if (files.Count > 0)
                {
                    sessionFile = files[0].FullName;
                }
                else
                {
                    sessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                }
            }
        }
        else
        {
            var files = Directory.GetFiles(sessionsDir, "session_*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            if (files.Count > 0)
            {
                sessionFile = files[0].FullName;
            }
            else
            {
                sessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            }
        }

        // Save last used session path
        File.WriteAllText(lastSessionFileLog, sessionFile);

        Action<LLMEvent>? currentLlmEventHandler = null;
        var session = await ChatSession.CreateAsync(
            apiKey, 
            modelName, 
            baseUrl, 
            loggerFactory, 
            sessionFile, 
            evt => currentLlmEventHandler?.Invoke(evt));

        Console.ForegroundColor = ConsoleColor.Cyan;
        var sessionTitle = await ChatSession.GetSessionTitleAsync(sessionFile);
        Console.WriteLine($"[Auto-loaded Session: {Path.GetFileName(sessionFile)} ({sessionTitle})]");
        Console.ResetColor();

        PrintHelp();

        var appCts = new CancellationTokenSource();
        CancellationTokenSource? currentInvocationCts = null;

        // 4. Console Interaction Loop
        while (!appCts.Token.IsCancellationRequested)
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

            using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(appCts.Token);
            currentInvocationCts = promptCts;

            using var keyListenCts = new CancellationTokenSource();
            var keyListenTask = Task.Run(async () =>
            {
                try
                {
                    while (!promptCts.Token.IsCancellationRequested && !keyListenCts.Token.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            var keyInfo = Console.ReadKey(intercept: true);
                            if (keyInfo.Key == ConsoleKey.Q && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("\n[Ctrl+Q: Cancelling current agent execution...]");
                                Console.ResetColor();
                                promptCts.Cancel();
                                break;
                            }
                        }
                        await Task.Delay(50);
                    }
                }
                catch
                {
                    // Ignore background key listener errors
                }
            });

            try
            {
                var contentInput = new Text(input);
                var rawArgs = new System.Collections.Generic.Dictionary<int, string>();
                var lastUnescaped = new System.Collections.Generic.Dictionary<int, string>();
                bool headerPrinted = false;
                bool toolDeltaStarted = false;

                currentLlmEventHandler = evt =>
                {
                    if (evt is Text t)
                    {
                        if (!headerPrinted)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("Assistant > ");
                            Console.ResetColor();
                            headerPrinted = true;
                        }
                        Console.Write(t.Value);
                    }
                    else if (evt is Reasoning r)
                    {
                        if (!headerPrinted)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write("Assistant [Reasoning] > ");
                            Console.ResetColor();
                            headerPrinted = true;
                        }
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(r.Thought);
                        Console.ResetColor();
                    }
                    else if (evt is ToolCall tc)
                    {
                        int index = tc.Index ?? 0;
                        if (!toolDeltaStarted)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.Write($"\n[LLM Stream ToolCall: {tc.Name} ");
                            Console.ResetColor();
                            toolDeltaStarted = true;
                        }
                        string newRaw = "";
                        if (tc.Arguments != null)
                        {
                            if (tc.Arguments is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var str))
                            {
                                newRaw = str;
                            }
                            else if (tc.Arguments is System.Text.Json.Nodes.JsonObject obj)
                            {
                                newRaw = obj.ToJsonString();
                            }
                            else
                            {
                                newRaw = tc.Arguments.ToString();
                            }
                        }

                        if (!rawArgs.TryGetValue(index, out var oldRaw))
                        {
                            oldRaw = "";
                        }

                        if (newRaw != oldRaw)
                        {
                            rawArgs[index] = newRaw;
                            var newUnescaped = UnescapePartialJson(newRaw);
                            if (!lastUnescaped.TryGetValue(index, out var oldUnescaped))
                            {
                                oldUnescaped = "";
                            }
                            
                            if (newUnescaped.Length > oldUnescaped.Length)
                            {
                                var delta = newUnescaped.Substring(oldUnescaped.Length);
                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.Write(delta);
                                Console.ResetColor();
                                lastUnescaped[index] = newUnescaped;
                            }
                        }
                    }
                };

                await foreach (var evt in session.Agent.InvokeStreamingAsync(contentInput, promptCts.Token))
                {
                    if (evt is ToolCallEvent toolCallEvent)
                    {
                        if (toolDeltaStarted)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine("]");
                            Console.ResetColor();
                            toolDeltaStarted = false;
                        }
                        var toolCall = toolCallEvent.ToolCall;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n[Agent executing tool: {toolCall.Name}({toolCall.ArgumentsObject})]");
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
                        if (!headerPrinted)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("Assistant > ");
                            Console.ResetColor();
                            Console.WriteLine(response.Response);
                        }
                        else
                        {
                            Console.WriteLine(); // Just end the streamed line
                        }
                    }
                    else if (evt is ErrorEvent error)
                    {
                        if (toolDeltaStarted)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine("]");
                            Console.ResetColor();
                            toolDeltaStarted = false;
                        }
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n[Error: {error.Error.Message}]");
                        Console.ResetColor();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[Execution cancelled]");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nException occurred: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                keyListenCts.Cancel();
                currentInvocationCts = null;
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
                {
                    var sessionsDir = Path.Combine(AppContext.BaseDirectory, "sessions");
                    var newSessionFile = Path.Combine(sessionsDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                    await session.StartNewAsync(newSessionFile);
                    
                    File.WriteAllText(Path.Combine(sessionsDir, "last_session.txt"), newSessionFile);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"New conversation session started: '{Path.GetFileName(newSessionFile)}'.");
                    Console.ResetColor();
                }
                break;

            case "/sessions":
            case "/list":
                {
                    var files = GetSessionFiles();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\n--- Available Sessions ---");
                    if (files.Count == 0)
                    {
                        Console.WriteLine("(No saved sessions found)");
                    }
                    else
                    {
                        for (int i = 0; i < files.Count; i++)
                        {
                            var file = files[i];
                            var isActive = file.Equals(session.SessionFile, StringComparison.OrdinalIgnoreCase);
                            var title = await ChatSession.GetSessionTitleAsync(file);
                            var marker = isActive ? "-> " : "   ";
                            var fileInfo = new FileInfo(file);
                            Console.WriteLine($"{marker}[{i}] {title} (Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
                        }
                    }
                    Console.WriteLine("--------------------------\n");
                    Console.ResetColor();
                }
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
                    Console.WriteLine("Error: Specify a session index or file to load (e.g., /load 0 or /load chat1.json).");
                    Console.ResetColor();
                    return;
                }
                try
                {
                    string targetFile = argument;
                    var files = GetSessionFiles();
                    if (int.TryParse(argument, out var sessionIndex))
                    {
                        if (sessionIndex >= 0 && sessionIndex < files.Count)
                        {
                            targetFile = files[sessionIndex];
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Error: Session index {sessionIndex} is out of range. Use /sessions to see available sessions.");
                            Console.ResetColor();
                            return;
                        }
                    }
                    else
                    {
                        var sessionsDir = Path.Combine(AppContext.BaseDirectory, "sessions");
                        var possiblePath = Path.Combine(sessionsDir, argument);
                        if (File.Exists(possiblePath))
                        {
                            targetFile = possiblePath;
                        }
                        else if (!File.Exists(targetFile))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Error: File '{argument}' not found.");
                            Console.ResetColor();
                            return;
                        }
                    }

                    await session.LoadAsync(targetFile);
                    
                    var sessionsDirUpdate = Path.Combine(AppContext.BaseDirectory, "sessions");
                    File.WriteAllText(Path.Combine(sessionsDirUpdate, "last_session.txt"), targetFile);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Session history loaded successfully from '{Path.GetFileName(targetFile)}'.");
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

    private static string UnescapePartialJson(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        int trailingBackslashes = 0;
        for (int i = raw.Length - 1; i >= 0; i--)
        {
            if (raw[i] == '\\')
                trailingBackslashes++;
            else
                break;
        }

        string toUnescape = (trailingBackslashes % 2 != 0) ? raw[..^1] : raw;

        return toUnescape.Replace("\\n", "\n")
                         .Replace("\\t", "\t")
                         .Replace("\\\"", "\"")
                         .Replace("\\\\", "\\");
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
        Console.WriteLine("  /new             - Start a new session with a new file");
        Console.WriteLine("  /sessions        - List all saved sessions (alias /list)");
        Console.WriteLine("  /load <index/fn> - Load a session by index or custom JSON file");
        Console.WriteLine("  /history         - List all messages in current session with indices");
        Console.WriteLine("  /revert <index>  - Truncate conversation history back to specific message index");
        Console.WriteLine("  /save <file>     - Save current session history to a JSON file");
        Console.WriteLine("  /logs            - Toggle verbose framework logging");
        Console.WriteLine("  /help            - Show this help menu");
        Console.WriteLine("--------------------------\n");
        Console.ResetColor();
    }
}

public class ToggleLoggerProvider : ILoggerProvider
{
    private static readonly object _fileLock = new();
    
    public ToggleLoggerProvider()
    {
        try
        {
            if (File.Exists("agent_diagnostics.log"))
            {
                File.Delete("agent_diagnostics.log");
            }
        }
        catch { }
    }

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

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{_category}] {formatter(state, exception)}";
            if (exception != null)
            {
                message += "\n" + exception.ToString();
            }

            if (logLevel >= LogLevel.Information)
            {
                lock (_fileLock)
                {
                    try
                    {
                        File.AppendAllText("agent_diagnostics.log", message + Environment.NewLine);
                    }
                    catch
                    {
                        // Ignore file write issues
                    }
                }
            }

            if (!_provider.Enabled) return;

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
