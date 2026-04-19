using AgentCore.Tooling;
using System.ComponentModel;

namespace AgentCore.BuiltInTools
{
    class MathTools
    {
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

        [Tool]
        [Description("Evaluates a mathematical expression using MathJS API. Supports arithmetic, algebra, trig, etc.")]
        public static async Task<string> EvaluateMath(
            [Description("Expression like '2+2*5', 'sqrt(16)', 'sin(pi/2)', 'log(100,10)'")] string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return "Error: empty expression.";

            var encoded = Uri.EscapeDataString(expression.Trim());
            var url = $"https://api.mathjs.org/v4/?expr={encoded}";

            using var response = await _httpClient.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();

            return response.IsSuccessStatusCode
                ? $"{expression} = {result.Trim()}"
                : $"Error: {result}";
        }

        [Tool]
        [Description("Calculates statistics (mean, median, mode, std dev) for a list of numbers.")]
        public static string CalculateStatistics(
            [Description("Comma-separated numbers like '1,2,3,4,5'")] string numbers)
        {
            var list = numbers.Split(',').Select(s => double.Parse(s.Trim())).OrderBy(x => x).ToList();
            if (list.Count == 0) return "Error: no numbers provided.";

            var mean = list.Average();
            var median = list.Count % 2 == 0
                ? (list[list.Count / 2 - 1] + list[list.Count / 2]) / 2
                : list[list.Count / 2];
            var groups = list.GroupBy(x => x).ToList();
            var maxFreq = groups.Max(g => g.Count());
            var mode = maxFreq == 1 ? "No mode" : string.Join(", ", groups.Where(g => g.Count() == maxFreq).Select(g => g.Key));
            var variance = list.Sum(x => Math.Pow(x - mean, 2)) / list.Count;
            var stdDev = Math.Sqrt(variance);

            return $"Count: {list.Count} | Sum: {list.Sum()} | Mean: {mean:F2} | Median: {median:F2} | Mode: {mode} | Std Dev: {stdDev:F2}";
        }
    }
}
