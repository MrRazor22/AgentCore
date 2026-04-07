using System;
using AgentCore.Tooling;
using AgentCore.CodingAgent;
using Microsoft.Extensions.Logging.Abstractions;

class Program {
    static void Main() {
        var toolRegistry = new ToolRegistry();
        toolRegistry.RegisterAll<MathLikeTools>();
        
        Console.WriteLine("=== Generated wrappers ===");
        var wrappers = ToolBridge.GenerateToolWrappers(toolRegistry.Tools);
        Console.WriteLine(wrappers);

        Console.WriteLine("=== FinalAnswer with tools ===");
        var toolExecutor = new ToolExecutor(toolRegistry, new ToolOptions(), NullLogger<ToolExecutor>.Instance);
        var exec = new RoslynScriptExecutor();
        exec.SendTools(toolRegistry.Tools, toolExecutor);
        var result = exec.Execute("FinalAnswer(\"Hi!\");");
        Console.WriteLine($"IsFinalAnswer: {result.IsFinalAnswer}");
        Console.WriteLine($"Output: {result.Output}");
        if (!string.IsNullOrWhiteSpace(result.Logs)) Console.WriteLine($"Logs: {result.Logs}");
    }
}

class MathLikeTools {
    [Tool("generate_random_int", "Generates a random int")]
    public static int GenerateRandomInt(int min = 0, int max = 100) => new Random().Next(min, max);

    [Tool("calculate_gcd", "Calculates GCD")]
    public static int CalculateGcd(int a, int b) => b == 0 ? a : CalculateGcd(b, a % b);
}
