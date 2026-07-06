using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.Diagnostics;
using AgentCore.LLM;
using AgentCore.Providers.Tornado;

var services = new ServiceCollection();

var client = new HttpDiagnosticsClient("http://localhost:5151");
services.AddSingleton(client);

services.AddAgentDiagnostics(options =>
{
    options.Observers.Add(client);
    options.Exporters.Add(client);
});

// Setup Tornado Provider (assuming LM Studio or similar local model server is running on 1234)
var provider = TornadoProvider.CreateLLMProvider("lmstudio", "qwen/qwen3.5-9b", new Uri("http://127.0.0.1:1234"));
services.AddSingleton<ILLMProvider>(provider);

var sp = services.BuildServiceProvider();

// Run background services
var hostedServices = sp.GetServices<IHostedService>();
foreach (var hs in hostedServices)
{
    await hs.StartAsync(default);
}

var tracer = sp.GetRequiredService<IAgentTracer>();

var agent = LLMAgent.Create("console-agent")
    .WithProvider(provider, new() { ContextWindow = 8000 })
    .AddDiagnostics()
    .Build();

var diagnosticAgent = new DiagnosticAgent(agent, tracer, "console-session-1");

Console.WriteLine("==================================================");
Console.WriteLine("Console Agent started.");
Console.WriteLine("Make sure AgentCore.Diagnostics.Explorer is running!");
Console.WriteLine("Type a message and press Enter (or 'exit' to quit):");
Console.WriteLine("==================================================");

while (true)
{
    Console.Write("> ");
    var msg = Console.ReadLine();
    if (string.IsNullOrEmpty(msg) || msg == "exit") break;
    
    try
    {
        var response = await diagnosticAgent.InvokeAsync(new Text(msg));
        Console.WriteLine($"Agent: {response.ForLlm()}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

// Stop background services
foreach (var hs in hostedServices)
{
    await hs.StopAsync(default);
}
