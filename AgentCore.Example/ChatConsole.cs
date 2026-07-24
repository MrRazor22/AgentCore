using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Spectre.Console;

namespace AgentCore.Example;

/// <summary>
/// Handles all console presentation logic using Spectre.Console.
/// Keeps Program.cs clean of formatting, menus, and Live display layout details.
/// </summary>
public sealed class ChatConsole
{
    public void ShowWelcomeScreen()
    {
        AnsiConsole.Clear();
        var rule = new Rule("[teal]AGENTCORE CODING ASSISTANT[/]")
        {
            Justification = Justify.Center
        };
        AnsiConsole.Write(rule);
        AnsiConsole.MarkupLine("[grey]An interactive ChatGPT-like coding assistant utilizing AgentCore workflow and Spectre.Console.[/]");
        AnsiConsole.WriteLine();
    }

    private static string GetRelativeTime(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 2) return "Yesterday";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return dateTime.ToString("yyyy-MM-dd");
    }

    public string SelectSessionMenu(List<string> sessionFiles, string currentSessionFile)
    {
        var menuChoices = new List<string> { "+ New session" };
        var fileMap = new Dictionary<string, string>();

        var sortedFiles = sessionFiles
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        for (int i = 0; i < sortedFiles.Count; i++)
        {
            var file = sortedFiles[i];
            var path = file.FullName;
            var isCurrent = path.Equals(currentSessionFile, StringComparison.OrdinalIgnoreCase);
            var prefix = isCurrent ? "[teal]❯ [/]" : "  ";
            var title = GetSessionTitleSync(path);
            var relativeTime = GetRelativeTime(file.LastWriteTime);

            var displayLabel = $"{prefix}{title.PadRight(40)}{relativeTime}";
            menuChoices.Add(displayLabel);
            fileMap[displayLabel] = path;
        }

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Select a session[/]")
                .PageSize(15)
                .AddChoices(menuChoices));

        if (selection == "+ New session") return "NEW";
        if (fileMap.TryGetValue(selection, out var targetFile))
        {
            return targetFile;
        }
        return "NEW";
    }

    public string ShowInteractiveMenu()
    {
        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]What do you want to do?[/]")
                .PageSize(10)
                .AddChoices(new[] {
                    "New session",
                    "Switch session",
                    "Revert",
                    "Toggle logs",
                    "Help",
                    "Exit"
                }));
        return choice;
    }

    public string? ReadUserInput()
    {
        AnsiConsole.WriteLine();
        var prompt = new TextPrompt<string>("[teal]>[/]")
            .PromptStyle("white")
            .AllowEmpty();
        
        try
        {
            return AnsiConsole.Prompt(prompt).Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task RenderStreamAsync(
        IAsyncEnumerable<IContent> eventStream, 
        Action<ILLMOutput> onLlmEvent,
        CancellationToken cancellationToken = default)
    {
        var reasoningSw = new System.Diagnostics.Stopwatch();
        bool isReasoningActive = false;
        bool hasPrintedAssistantHeader = false;

        void EndReasoningIfActive()
        {
            if (isReasoningActive)
            {
                isReasoningActive = false;
                reasoningSw.Stop();
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]✓[/] Thought for {reasoningSw.Elapsed.TotalSeconds:F1}s");
            }
        }

        Action<ILLMOutput> originalHandler = evt =>
        {
            onLlmEvent(evt);
            
            if (evt is TextDelta t)
            {
                EndReasoningIfActive();

                if (!hasPrintedAssistantHeader)
                {
                    hasPrintedAssistantHeader = true;
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[bold green]Assistant[/]");
                }
                AnsiConsole.Write(new Spectre.Console.Text(t.Value));
            }
            else if (evt is ReasoningDelta r)
            {
                if (!isReasoningActive)
                {
                    isReasoningActive = true;
                    reasoningSw.Restart();
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[grey]Thinking...[/]");
                }
                AnsiConsole.Write(new Spectre.Console.Text(r.Thought, new Style(foreground: Color.Grey)));
            }
            else if (evt is ToolCallDelta tc)
            {
                EndReasoningIfActive();
                if (!string.IsNullOrEmpty(tc.NameDelta))
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Markup($"[yellow]●[/] {tc.NameDelta}(");
                }

                if (!string.IsNullOrEmpty(tc.ArgumentsDelta))
                {
                    AnsiConsole.Write(new Spectre.Console.Text(tc.ArgumentsDelta));
                }
            }
        };

        Program.CurrentLlmEventHandler = originalHandler;

        try
        {
            await foreach (var content in eventStream.WithCancellation(cancellationToken))
            {
                if (content is ToolResult tr)
                {
                    EndReasoningIfActive();

                    var resultStr = tr.Result?.ForLlm() ?? "";
                    var size = resultStr.Length;
                    var sizeStr = size >= 1024 ? $"{size / 1024.0:F1} KB" : $"{size} bytes";
                    AnsiConsole.MarkupLine($"\n[green]✓[/] {tr.CallId} ({sizeStr})");
                }
            }
        }
        catch (Exception ex)
        {
            EndReasoningIfActive();
            AnsiConsole.MarkupLine($"\n[red]✗[/] {Markup.Escape(ex.Message)}");
        }
        finally
        {
            EndReasoningIfActive();
            Program.CurrentLlmEventHandler = null;
            AnsiConsole.WriteLine();
        }
    }

    public class RevertOption
    {
        public string DisplayText { get; set; } = "";
        public int Index { get; set; }
        public override string ToString() => DisplayText;
    }

    public int? PromptRevertMenu(IReadOnlyList<Message> history)
    {
        var options = new List<RevertOption>();

        for (int i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            if (msg.Role == Role.User)
            {
                var content = string.Join(" ", msg.Contents.Select(c => c.ForLlm()));
                if (content.Length > 60) content = content[..57] + "...";
                
                options.Add(new RevertOption { DisplayText = $"\"{content}\"", Index = i });
            }
        }

        if (options.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No revertable points found in this session.[/]");
            return null;
        }

        options.Reverse();

        var cancelOption = new RevertOption { DisplayText = "← Cancel", Index = -1 };
        options.Add(cancelOption);

        AnsiConsole.WriteLine();
        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<RevertOption>()
                .Title("[yellow]Rewind to before:[/]")
                .PageSize(15)
                .AddChoices(options));

        if (selection == cancelOption) return null;
        return selection.Index;
    }

    public void PrintHelp()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]Commands:[/]");
        AnsiConsole.MarkupLine("  [yellow]/menu[/]           - Open session menu and settings");
        AnsiConsole.MarkupLine("  [yellow]/revert[/]         - Revert conversation to an earlier message");
        AnsiConsole.MarkupLine("  [yellow]/logs[/]           - Toggle diagnostic logging");
        AnsiConsole.MarkupLine("  [yellow]/help[/]           - Show available commands");
        AnsiConsole.WriteLine();
    }

    public void PrintNormalChat(IReadOnlyList<Message> history)
    {
        foreach (var msg in history)
        {
            var content = string.Join(" ", msg.Contents.Select(c => c.ForLlm()));
            if (msg.Role == Role.User)
            {
                AnsiConsole.MarkupLine("[bold teal]You[/]");
                AnsiConsole.WriteLine(content);
                AnsiConsole.WriteLine();
            }
            else if (msg.Role == Role.Assistant)
            {
                AnsiConsole.MarkupLine("[bold green]Assistant[/]");
                AnsiConsole.WriteLine(content);
                AnsiConsole.WriteLine();
            }
        }
    }

    public void PrintDiagnosticHistory(IReadOnlyList<Message> history)
    {
        for (int i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            var content = string.Join(" ", msg.Contents.Select(c => c.ForLlm()));
            var roleName = msg.Role == Role.User ? "User" : "Assistant";
            AnsiConsole.MarkupLine($"[{i}] [teal]{roleName}:[/] {Markup.Escape(content)}");
        }
        AnsiConsole.WriteLine();
    }

    private static string GetSessionTitleSync(string filePath)
    {
        if (!File.Exists(filePath)) return "New Session";
        try
        {
            var json = File.ReadAllText(filePath);
            var messages = System.Text.Json.JsonSerializer.Deserialize<List<Message>>(json);
            if (messages != null && messages.Count > 0)
            {
                var firstUserMsg = messages.FirstOrDefault(m => m.Role == Role.User) ?? messages.FirstOrDefault();
                if (firstUserMsg != null)
                {
                    var summary = string.Join(" ", firstUserMsg.Contents.Select(c => c.ForLlm()));
                    if (summary.Length > 50) summary = summary[..47] + "...";
                    return summary;
                }
            }
        }
        catch { }
        return "Empty Session";
    }
}
