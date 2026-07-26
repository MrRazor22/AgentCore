using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeSharp.UI
{
    public class ConsoleSpinner : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private Task? _task;

        public void Start()
        {
            _task = Task.Run(async () =>
            {
                var spinner = Spinner.Known.Dots;
                int i = 0;
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write($"\r{spinner.Frames[i]} ");
                        Console.ResetColor();
                        i = (i + 1) % spinner.Frames.Count;
                        await Task.Delay(spinner.Interval, _cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            });
        }

        public void Stop()
        {
            _cts.Cancel();
            try
            {
                _task?.Wait();
            }
            catch (Exception) { }
            Console.Write("\r   \r");
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }
    }
}
