// File: Program.cs 
namespace TestApp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            //await ChatBotAgent.RunAsync();
            await StructredResponseAgent.RunAsync();
        }
    }
}
