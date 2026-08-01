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

namespace CodeSharp.UI
{
    /// <summary>
    /// Wraps a completed <see cref="ToolResult"/> as an <see cref="ILLMOutput"/> so it can be
    /// written into the same channel as raw LLM token deltas. Because the channel is FIFO and has
    /// a single reader, this guarantees the result is rendered strictly after every preceding LLM
    /// token for that iteration — with no locking or timing assumptions required.
    /// </summary>
    internal sealed record ToolResultOutput(ToolResult Result) : ILLMOutput;

    public sealed class ConsoleStreamRenderer
    {
        private readonly Stopwatch _thinkingSw = new();
        private ConsoleSpinner? _spinner;
        private ConsoleTreeWriter? _thinkingWriter;
        private readonly List<AccumulatedToolCall> _toolCalls = new();

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

        private class AccumulatedToolCall
        {
            public string Id { get; set; } = "";
            public int? Index { get; set; }
            public StringBuilder Name { get; } = new();
            public StringBuilder Arguments { get; } = new();
            public bool Finalized { get; set; }
        }

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
            FinalizeAllToolCalls();
            _answerStarted = false;
        }

        public void Write(ILLMOutput output)
        {
            if (output is FinishReason)
            {
                ResetForNextStep();
                return;
            }

            if (output is ReasoningDelta reasoning)
            {
                FinalizeAllToolCalls();

                string thought = reasoning.Thought;
                if (string.IsNullOrEmpty(thought))
                {
                    return;
                }

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

                _thinkingWriter.Write(thought);
            }
            else if (output is TextDelta text)
            {
                if (!_answerStarted)
                {
                    StopSpinner();

                    bool hadThinking = _thinkingWriter != null;
                    FinalizeThinking();
                    FinalizeAllToolCalls();

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
            }
            else if (output is ToolCallDelta tc)
            {
                StopSpinner();
                FinalizeThinking();

                AccumulatedToolCall? toolCall = null;
                if (!string.IsNullOrEmpty(tc.Id))
                {
                    toolCall = _toolCalls.FirstOrDefault(t => t.Id == tc.Id);
                }
                else if (tc.Index.HasValue)
                {
                    toolCall = _toolCalls.FirstOrDefault(t => t.Index == tc.Index.Value);
                }

                if (toolCall == null)
                {
                    // A new tool call is starting. Finalize previous ones
                    FinalizeAllToolCalls();

                    toolCall = new AccumulatedToolCall
                    {
                        Id = tc.Id ?? "",
                        Index = tc.Index
                    };
                    _toolCalls.Add(toolCall);
                }

                if (!string.IsNullOrEmpty(tc.NameDelta))
                {
                    toolCall.Name.Append(tc.NameDelta);
                }

                if (!string.IsNullOrEmpty(tc.ArgumentsDelta))
                {
                    toolCall.Arguments.Append(tc.ArgumentsDelta);
                }
            }
            else if (output is ToolResultOutput tro)
            {
                WriteToolResultCore(tro.Result);
            }
        }

        public void Complete()
        {
            StopSpinner();
            FinalizeThinking();
            FinalizeAllToolCalls();
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

        private void FinalizeAllToolCalls()
        {
            foreach (var toolCall in _toolCalls)
            {
                FinalizeToolCall(toolCall);
            }
            _toolCalls.Clear();
        }

        private void FinalizeToolCall(AccumulatedToolCall toolCall)
        {
            if (toolCall.Finalized) return;
            toolCall.Finalized = true;

            var name = toolCall.Name.ToString().Trim();
            if (string.IsNullOrEmpty(name)) return;

            // Record call-ID → name so WriteToolResultCore can display the correct tool name.
            if (!string.IsNullOrEmpty(toolCall.Id))
                _toolCallNames[toolCall.Id] = name;

            var rawArgs = toolCall.Arguments.ToString();

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold yellow]Tool Call:[/] [bold cyan]{Markup.Escape(name)}[/]");

            JsonObject? jsonObject = null;
            if (!string.IsNullOrWhiteSpace(rawArgs))
            {
                try
                {
                    jsonObject = JsonNode.Parse(rawArgs)?.AsObject();
                }
                catch { }
            }

            var dummyCall = new ToolCall(toolCall.Id, name, jsonObject ?? new JsonObject());
            var displayInfo = _formatter.FormatCall(dummyCall);

            var summary = displayInfo.ArgSummary;
            // Compact rendering: truncate normal stream representation to MaxArgLength
            if (summary.Length > MaxArgLength)
            {
                summary = summary[..MaxArgLength] + "...";
            }

            AnsiConsole.MarkupLine($"└─ {Markup.Escape(summary)}");
        }

        /// <summary>
        /// Renders a tool result. Called from <see cref="Write"/> when a
        /// <see cref="ToolResultOutput"/> arrives off the channel, which guarantees this runs
        /// after all preceding LLM token deltas for the same iteration have been rendered.
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
