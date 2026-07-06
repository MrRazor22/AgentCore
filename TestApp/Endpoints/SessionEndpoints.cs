using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TestApp.Services;

namespace TestApp.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/sessions");

        group.MapGet("/", async (SessionStore store) =>
        {
            var sessions = await store.GetSessionsAsync();
            return Results.Ok(sessions);
        });

        group.MapGet("/{id}", async (string id) =>
        {
            var path = Path.Combine(Path.GetFullPath("sessions"), $"{id}.json");
            if (!File.Exists(path))
            {
                return Results.Ok(Array.Empty<object>());
            }
            var content = await File.ReadAllTextAsync(path);
            return Results.Content(content, "application/json");
        });

        group.MapDelete("/{id}", (string id, SessionStore store) =>
        {
            store.DeleteSession(id);
            return Results.NoContent();
        });
    }
}
