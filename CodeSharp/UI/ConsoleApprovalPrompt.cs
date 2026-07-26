using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using Spectre.Console;

namespace CodeSharp.UI;

/// <summary>
/// Spectre.Console implementation of IApprovalPrompt for interactive terminal confirmation.
/// Uses async confirmation inside a styled Spectre Panel with CancellationToken support and thread-safe console serialization.
/// </summary>
public sealed class ConsoleApprovalPrompt : Layers.IApprovalPrompt
{
    private static readonly SemaphoreSlim _promptLock = new(1, 1);

    public async Task<bool> RequestApprovalAsync(ToolCall call, CancellationToken ct)
    {
        await _promptLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var argsJson = call.Arguments?.ToString() ?? "{}";

            var grid = new Grid();
            grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
            grid.AddColumn(new GridColumn());

            grid.AddRow("[bold grey]Tool:[/]", $"[bold cyan]{Markup.Escape(call.Name)}[/]");
            grid.AddRow("[bold grey]Arguments:[/]", $"[yellow]{Markup.Escape(argsJson)}[/]");

            var panel = new Panel(grid)
            {
                Header = new PanelHeader($"[bold yellow] ⚠ Approval Required [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow),
                Padding = new Padding(1, 0, 1, 0)
            };

            AnsiConsole.WriteLine();
            AnsiConsole.Write(panel);

            return await AnsiConsole.ConfirmAsync(
                "  [bold green]Allow execution?[/]",
                defaultValue: false,
                cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            _promptLock.Release();
        }
    }
}
