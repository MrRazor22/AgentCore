using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeSharp.UI
{
    public sealed class ConsoleTreeWriter
    {
        private readonly Style _style;
        private readonly string _indent;

        public ConsoleTreeWriter(Style style, string indent = "     ")
        {
            _style = style;
            _indent = indent;
        }

        public void Start(string title)
        {
            AnsiConsole.MarkupLine(title);
            AnsiConsole.Write("  └  ");
        }

        public void Write(string content)
        {
            if (string.IsNullOrEmpty(content)) return;

            string formatted = content
                .Replace("\r\n", "\n")
                .Replace("\n", "\n" + _indent);

            AnsiConsole.Write(new Spectre.Console.Text(formatted, _style));
        }
    }
}
