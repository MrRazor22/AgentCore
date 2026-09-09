using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using Spectre.Console;
using AgentText = AgentCore.LLM.Chat.Text;

namespace CodeSharp.UI;

public class ChatUI
{
    private readonly IAgent _agent;
    private readonly string _modelName;
    private readonly string _workspacePath;
    private readonly IToolDisplayFormatter _formatter;

    public ChatUI(IAgent agent, string modelName, string workspacePath, IToolDisplayFormatter formatter)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _modelName = modelName;
        _workspacePath = workspacePath;
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        string versionStr = assemblyVersion != null ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}" : "1.0.0";

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold purple]CodeSharp v{versionStr}[/] | [grey]Model:[/] {_modelName} | [grey]Workspace:[/] {_workspacePath}");
        AnsiConsole.MarkupLine("[grey]Tip: Press [white]Esc[/] to stop a response in progress.[/]");
        AnsiConsole.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            var promptStyle = new Style(Color.White);
            var input = AnsiConsole.Prompt(
                new TextPrompt<string>("❯ ")
                    .PromptStyle(promptStyle)
                    .AllowEmpty()
            )?.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var totalSw = Stopwatch.StartNew();

            using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var keyListenerTask = Task.Run(async () =>
            {
                while (!turnCts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            turnCts.Cancel();
                            break;
                        }
                    }
                    try
                    {
                        await Task.Delay(50, turnCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            });

            var renderer = new ConsoleStreamRenderer(_formatter);

            try
            {
                try
                {
                    await foreach (var evt in _agent.InvokeStreamingAsync(new AgentText(input), turnCts.Token))
                    {
                        renderer.Write(evt);
                    }
                }
                finally
                {
                    renderer.Complete();
                    turnCts.Cancel();
                    try
                    {
                        await keyListenerTask;
                    }
                    catch (Exception) { }
                }

                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[grey]Done in {FormatDuration(totalSw.Elapsed)}[/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]Response stopped.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"\n[bold red]Error during invocation:[/] {ex.Message}");
            }

            AnsiConsole.WriteLine();
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }
        return $"{duration.TotalSeconds:F0}s";
    }
}
