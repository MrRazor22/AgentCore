// File: Program.cs 
namespace TestApp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("╔════════════════════════════════════════╗");
            Console.WriteLine("║       AgentCore Test Applications      ║");
            Console.WriteLine("╠════════════════════════════════════════╣");
            Console.WriteLine("║  1. ChatBot Agent (LLM with tools)    ║");
            Console.WriteLine("║  2. MCP Server Demo                   ║");
            Console.WriteLine("║  3. Coding Agent                      ║");
            Console.WriteLine("╚════════════════════════════════════════╝");
            Console.Write("\nSelect agent (1-3): ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await ChatBotAgent.RunAsync();
                    break;
                case "2":
                    Console.WriteLine("McpTestAgent currently disabled.");
                    break;
                case "3":
                    Console.WriteLine("CodingTestAgent currently disabled.");
                    break;
                default:
                    Console.WriteLine("Invalid choice. Running ChatBot by default...");
                    await ChatBotAgent.RunAsync();
                    break;
            }
        }
    }
}
