using AgentCore.LLM.Chat;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeSharp.UI
{
    public sealed class ConsoleStreamRenderer
    {
        private readonly Stopwatch _thinkingSw = new();
        private ConsoleSpinner? _spinner;
        private ConsoleTreeWriter? _thinkingWriter;
        private bool _answerStarted;

        public ConsoleStreamRenderer()
        {
            _spinner = new ConsoleSpinner();
            _spinner.Start();
        }

        private void StopSpinner()
        {
            _spinner?.Dispose();
            _spinner = null;
        }

        public void Write(ILLMOutput output)
        {
            if (output is ReasoningDelta reasoning)
            {
                if (_thinkingWriter == null)
                {
                    StopSpinner();
                    AnsiConsole.WriteLine();
                    _thinkingSw.Start();
                    var style = new Style(Color.Grey, decoration: Decoration.Italic);
                    _thinkingWriter = new ConsoleTreeWriter(style);
                    _thinkingWriter.Start("Thinking...");
                }

                _thinkingWriter.Write(reasoning.Thought);
            }
            else if (output is TextDelta text)
            {
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
                    AnsiConsole.Write(trimmed);
                }
                else
                {
                    AnsiConsole.Write(text.Value);
                }
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
    }
}
