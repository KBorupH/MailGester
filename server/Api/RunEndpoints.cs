using MailGester.Data;
using MailGester.Digest;
using MailGester.Runtime;

namespace MailGester.Api;

public static class RunEndpoints
{
    public static void MapRun(this RouteGroupBuilder api)
    {
        api.MapGet("/status", async (RunStatus status, DigestRunner runner, CancellationToken ct) =>
            Results.Ok(new { run = status.Snapshot(), configured = await runner.IsConfiguredAsync(ct) }));

        api.MapPost("/run", async (DigestRunner runner, RunCoordinator coordinator, CancellationToken ct) =>
        {
            if (runner.IsBusy)
            {
                return Results.Conflict(new { error = "A digest is already running." });
            }

            if (!await runner.IsConfiguredAsync(ct))
            {
                return Results.BadRequest(new { error = "Add an enabled mail account and a Discord webhook first." });
            }

            coordinator.RequestRunNow();
            return Results.Accepted();
        });

        api.MapGet("/digests", async (DigestRepository history, int? take, int? skip, CancellationToken ct) =>
            Results.Ok(await history.ListAsync(Math.Clamp(take ?? 20, 1, 100), Math.Max(skip ?? 0, 0), ct)));

        api.MapGet("/digests/{id:long}", async (long id, DigestRepository history, CancellationToken ct) =>
            await history.GetAsync(id, ct) is { } digest ? Results.Ok(digest) : Results.NotFound());

        api.MapGet("/ollama", async (OllamaAdmin ollama, CancellationToken ct) =>
            Results.Ok(new { check = await ollama.CheckAsync(ct), pull = ollama.Pull }));

        api.MapPost("/ollama/pull", (OllamaAdmin ollama) =>
            ollama.StartPull() ? Results.Accepted() : Results.Conflict(new { error = "A pull is already running." }));

        api.MapPost("/discord/test", async (DiscordNotifier discord, CancellationToken ct) =>
        {
            try
            {
                await discord.SendTestAsync(ct);
                return Results.Ok(new { ok = true, error = (string?)null });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new { ok = false, error = ex.Message });
            }
        });
    }
}
