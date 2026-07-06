using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TestApp.Endpoints;
using TestApp.Services;
using AgentCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Register Core Services
builder.Services.AddSingleton<AgentEventBus>();
builder.Services.AddSingleton<IAgentEventBus>(sp => sp.GetRequiredService<AgentEventBus>());
builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<AgentEventBus>());
builder.Services.AddSingleton<IApprovalService, ApprovalService>();
builder.Services.AddSingleton<SessionStore>(_ => new SessionStore("sessions"));

var client = new HttpDiagnosticsClient("http://localhost:5151");
builder.Services.AddSingleton(client);

builder.Services.AddAgentDiagnostics(options =>
{
    options.Observers.Add(client);
    options.Exporters.Add(client);
    options.Exporters.Add(new JsonTraceStore("traces"));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Map Endpoint Groups
app.MapSessionEndpoints();
app.MapApprovalEndpoints();
app.MapChatEndpoints();

app.Run();
