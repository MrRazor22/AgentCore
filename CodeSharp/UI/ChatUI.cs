using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using CodeSharp.Layers;
using Spectre.Console;
using AgentText = AgentCore.LLM.Chat.Text;

namespace CodeSharp.UI;

public class ChatUI
{
    private readonly IAgent _agent;
    private readonly string _modelName;
    private readonly string _workspacePath;
    private readonly StreamingLLMLayer _streamingLayer;

    public ChatUI(IAgent agent, string modelName, string workspacePath, StreamingLLMLayer streamingLayer)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _modelName = modelName;
        _workspacePath = workspacePath;
        _streamingLayer = streamingLayer ?? throw new ArgumentNullException(nameof(streamingLayer));
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Get dynamic version
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        string versionStr = assemblyVersion != null ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}" : "1.0.0";

        // Render beautiful header
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

            var channel = Channel.CreateUnbounded<ILLMOutput>(new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = false
            });

            var renderTask = RenderStreamAsync(channel.Reader, turnCts.Token);
            _streamingLayer.Writer = channel.Writer;

            try
            {
                try
                {
                    await foreach (var content in _agent.InvokeStreamingAsync(new AgentText(input), turnCts.Token))
                    {
                        // Stream execution is driven by pulling items from the high-level enumerator.
                        // Raw token rendering is handled out-of-band by the channel observer.
                    }
                }
                finally
                {
                    _streamingLayer.Writer = null;
                    channel.Writer.TryComplete();
                    try
                    {
                        await renderTask;
                    }
                    catch (Exception)
                    {
                        // UI/rendering failure must not replace the agent failure
                    }

                    turnCts.Cancel(); // Ensure task closes if exited normally
                    try
                    {
                        await keyListenerTask;
                    }
                    catch (Exception) { }
                }

                // End of turn: print done duration in grey
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

    private async Task RenderStreamAsync(ChannelReader<ILLMOutput> reader, CancellationToken cancellationToken)
    {
        var renderer = new ConsoleStreamRenderer();

        try
        {
            await foreach (var output in reader.ReadAllAsync(cancellationToken))
            {
                renderer.Write(output);
            }
        }
        finally
        {
            renderer.Complete();
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





