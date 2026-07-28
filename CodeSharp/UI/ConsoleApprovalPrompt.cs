using AgentCore.LLM.Chat;
using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

namespace CodeSharp.UI;

/// <summary>
/// A decoupled, human-readable approval prompt that formats and renders tool calls.
/// </summary>
public sealed class ConsoleApprovalPrompt : Layers.IApprovalPrompt
{
    private static readonly SemaphoreSlim _promptLock = new(1, 1);
    private readonly IToolDisplayFormatter _formatter;

    public ConsoleApprovalPrompt(IToolDisplayFormatter formatter)
    {
        _formatter = formatter ?? throw new System.ArgumentNullException(nameof(formatter));
    }

    public async Task<bool> RequestApprovalAsync(ToolCall call, CancellationToken ct)
    {
        await _promptLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var display = _formatter.FormatCall(call);

            while (true)
            {
                AnsiConsole.WriteLine();
                
                // Render Title and Target
                AnsiConsole.MarkupLine($"[bold yellow]{Markup.Escape(display.DisplayName)}[/]");
                
                // Render Target inline (untruncated command or parameters)
                AnsiConsole.MarkupLine($"  [bold white]{Markup.Escape(display.ArgSummary)}[/]");
                AnsiConsole.WriteLine();

                var hasDetails = !string.IsNullOrEmpty(display.LongDetails);
                var choices = hasDetails
                    ? new[] { "Allow", "Deny", "View Details" }
                    : new[] { "Allow", "Deny" };

                var choice = await MenuPrompt.SelectAsync(
                    title: "  [grey]Select action:[/]",
                    choices: choices,
                    ct: ct
                ).ConfigureAwait(false);

                if (choice == "Allow")
                {
                    return true;
                }
                if (choice == "Deny")
                {
                    return false;
                }
                if (choice == "View Details" && hasDetails)
                {
                    // Render details block indented with tree borders
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("  [grey]┌─ Details ──────────────────────────────────────────[/]");
                    var lines = display.LongDetails!.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        AnsiConsole.MarkupLine($"  [grey]│[/] {Markup.Escape(line)}");
                    }
                    AnsiConsole.MarkupLine("  [grey]└────────────────────────────────────────────────────[/]");
                    AnsiConsole.WriteLine();
                    
                    // Loop again to return to the menu with option to view again
                }
            }
        }
        finally
        {
            _promptLock.Release();
        }
    }
}
