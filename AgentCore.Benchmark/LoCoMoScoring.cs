using System.Text.RegularExpressions;

namespace AgentCore.Benchmark;

public static class LoCoMoScoring
{
    public static double Score(string prediction, string groundTruth, int category)
    {
        // Category 5 is adversarial - requires exact semantic match of 'not mentioned' concept
        if (category == 5)
        {
            var p = prediction.ToLowerInvariant();
            if (p.Contains("not mentioned") || p.Contains("i don't know") || p.Contains("do not know") || p.Contains("cannot answer"))
            {
                return 1.0;
            }
            return 0.0;
        }

        return TokenF1(prediction, groundTruth);
    }

    private static double TokenF1(string prediction, string groundTruth)
    {
        var predTokens = Normalize(prediction).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var truthTokens = Normalize(groundTruth).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (predTokens.Length == 0 && truthTokens.Length == 0) return 1.0;
        if (predTokens.Length == 0 || truthTokens.Length == 0) return 0.0;

        var common = predTokens.Intersect(truthTokens, StringComparer.OrdinalIgnoreCase).Count();

        if (common == 0) return 0.0;

        var precision = (double)common / predTokens.Length;
        var recall = (double)common / truthTokens.Length;

        return 2 * (precision * recall) / (precision + recall);
    }

    private static string Normalize(string s)
    {
        // Lowercase, remove punctuation, reduce extra spaces
        s = s.ToLowerInvariant();
        s = Regex.Replace(s, @"[^\w\s]", "");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }
}
