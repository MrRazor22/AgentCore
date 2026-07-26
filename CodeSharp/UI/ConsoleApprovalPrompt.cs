using AgentCore.LLM.Chat;
using Spectre.Console;

namespace CodeSharp.UI;

/// <summary>
/// Minimalist interactive approval prompt using reusable MenuPrompt helper.
/// No heavy panels or boxes — clean inline tool accenting with arrow-key Yes/No selection.
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
            var compactArgs = argsJson.Length > 150 ? argsJson[..150] + "..." : argsJson;

            // Minimal inline header with subtle color accents (no question mark prefix)
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold yellow]Approval Required:[/] [bold cyan]{Markup.Escape(call.Name)}[/] [grey]({Markup.Escape(compactArgs)})[/]");

            var choice = await MenuPrompt.SelectAsync(
                title: "  [grey]Allow execution?[/]",
                choices: new[] { "Yes", "No" },
                ct: ct
            ).ConfigureAwait(false);

            return choice == "Yes";
        }
        finally
        {
            _promptLock.Release();
        }
    }
}
