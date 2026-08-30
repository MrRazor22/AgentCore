using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AgentText = AgentCore.LLM.Chat.Text;

namespace CodeSharp.UI
{
    public sealed class ConsoleStreamRenderer
    {
        private readonly Stopwatch _thinkingSw = new();
        private ConsoleSpinner? _spinner;
        private ConsoleTreeWriter? _thinkingWriter;

        /// <summary>Maps tool-call ID → tool name, populated as each call is finalized.</summary>
        private readonly Dictionary<string, string> _toolCallNames = new();

        private bool _answerStarted;

        /// <summary>
        /// Threshold for tool result display. Results with this many lines or fewer are shown in
        /// full; results above this show a count-only summary. Keeps the terminal an execution
        /// trace rather than a file viewer.
        /// </summary>
        private const int MaxResultLines = 8;

        /// <summary>Maximum characters shown for a single-line tool-call argument value.</summary>
        private const int MaxArgLength = 120;

        private readonly IToolDisplayFormatter _formatter;

        public ConsoleStreamRenderer(IToolDisplayFormatter formatter)
        {
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _spinner = new ConsoleSpinner();
            _spinner.Start();
        }

        private void StopSpinner()
        {
            _spinner?.Dispose();
            _spinner = null;
        }

        public void ResetForNextStep()
        {
            FinalizeThinking();
            _answerStarted = false;
        }

        public void Write(IContent output)
        {
            switch (output)
            {
                case Reasoning reasoning:
                    if (_thinkingWriter == null)
                    {
                        StopSpinner();
                        AnsiConsole.WriteLine();
                        _thinkingSw.Reset();
                        _thinkingSw.Start();
                        var style = new Style(Color.Grey, decoration: Decoration.Italic);
                        _thinkingWriter = new ConsoleTreeWriter(style);
                        _thinkingWriter.Start("Thinking...");
                    }
                    if (!string.IsNullOrEmpty(reasoning.Thought))
                    {
                        _thinkingWriter.Write(reasoning.Thought);
                    }
                    break;

                case AgentText text:
                    if (!_answerStarted)
                    {
                        StopSpinner();
                        bool hadThinking = _thinkingWriter != null;
                        FinalizeThinking();
                        if (!hadThinking)
                        {
                            AnsiConsole.WriteLine();
                        }
                        _answerStarted = true;
                        var trimmed = text.Value.TrimStart('\r', '\n');
                        AnsiConsole.Write(new Spectre.Console.Text(trimmed));
                    }
                    else
                    {
                        AnsiConsole.Write(new Spectre.Console.Text(text.Value));
                    }
                    break;

                case ToolCall tc:
                    StopSpinner();
                    FinalizeThinking();
                    RenderToolCall(tc);
                    break;

                case ToolResult tr:
                    WriteToolResultCore(tr);
                    break;
            }
        }

        public void Complete()
        {
            StopSpinner();
            FinalizeThinking();
        }

        private void FinalizeThinking()
        {
            if (_thinkingWriter != null)
            {
                _thinkingSw.Stop();
                _thinkingWriter = null;
                double seconds = _thinkingSw.Elapsed.TotalSeconds;
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[grey]Thought for {seconds:F0}s[/]");
                AnsiConsole.WriteLine();
            }
        }

        private void RenderToolCall(ToolCall tc)
        {
            var name = tc.Name.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (!string.IsNullOrEmpty(tc.Id))
                _toolCallNames[tc.Id] = name;

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold yellow]Tool Call:[/] [bold cyan]{Markup.Escape(name)}[/]");

            var displayInfo = _formatter.FormatCall(tc);
            var summary = displayInfo.ArgSummary;
            if (summary.Length > MaxArgLength)
            {
                summary = summary[..MaxArgLength] + "...";
            }

            AnsiConsole.MarkupLine($"└─ {Markup.Escape(summary)}");
        }

        /// <summary>
        /// Renders a tool result.
        /// </summary>
        private void WriteToolResultCore(ToolResult result)
        {
            _toolCallNames.TryGetValue(result.CallId, out var name);
            name = string.IsNullOrEmpty(name) ? result.CallId : name;

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold green]Tool Result:[/] [bold cyan]{Markup.Escape(name)}[/]");

            var content = result.Result?.ForLlm() ?? "";

            // Denial — yellow/dim; important agent events, always shown
            if (content.StartsWith("[DENIED]", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"└─ [yellow dim]{Markup.Escape(content)}[/]");
                return;
            }

            // Tool execution error — red
            if (content.StartsWith("Error calling tool '", StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine($"└─ [red]{Markup.Escape(content)}[/]");
                return;
            }

            // Empty result
            if (string.IsNullOrWhiteSpace(content))
            {
                AnsiConsole.MarkupLine("└─ [grey](empty)[/]");
                return;
            }

            var resultText = (_formatter is CompositeToolDisplayFormatter composite)
                ? composite.FormatResult(name, content)
                : _formatter.FormatResult(content);

            AnsiConsole.MarkupLine($"└─ {Markup.Escape(resultText)}");
        }
    }
}
