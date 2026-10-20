using MailGester.Data;

namespace MailGester.Api;

public sealed record DiscordDto(string? WebhookUrl, string PingRoleId, bool WebhookConfigured);

public sealed record SettingsDto(DigestOptions Digest, OllamaOptions Ollama, DiscordDto Discord, GraphOptions Graph);

public static class SettingsEndpoints
{
    public static void MapSettings(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", (SettingsService settings) => Results.Ok(ToDto(settings.Current)));

        api.MapPut("/settings", async (SettingsDto dto, SettingsService settings, CancellationToken ct) =>
        {
            var errors = Validate(dto);
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var current = settings.Current;

            // An empty webhook means "keep the stored one", so the secret never has to round trip through the browser.
            var webhook = string.IsNullOrWhiteSpace(dto.Discord.WebhookUrl)
                ? current.Discord.WebhookUrl
                : dto.Discord.WebhookUrl.Trim();

            dto.Digest.PriorityKeywords = dto.Digest.PriorityKeywords
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await settings.SaveAsync(new AppSettings
            {
                Digest = dto.Digest,
                Ollama = dto.Ollama,
                Discord = new DiscordOptions { WebhookUrl = webhook, PingRoleId = dto.Discord.PingRoleId.Trim() },
                Graph = dto.Graph
            }, ct);

            return Results.Ok(ToDto(settings.Current));
        });
    }

    private static SettingsDto ToDto(AppSettings s) => new(
        s.Digest,
        s.Ollama,
        new DiscordDto(null, s.Discord.PingRoleId, !string.IsNullOrWhiteSpace(s.Discord.WebhookUrl)),
        s.Graph);

    private static List<string> Validate(SettingsDto dto)
    {
        var errors = new List<string>();
        var d = dto.Digest;
        var o = dto.Ollama;

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(d.TimeZone, out _))
        {
            errors.Add("The time zone is not known on this server. Use an IANA name like Europe/Copenhagen.");
        }

        if (!TimeOnly.TryParse(d.RunAt, out _))
        {
            errors.Add("Run at must be a time like 07:30.");
        }

        Range(errors, "First run lookback (hours)", d.FirstRunLookbackHours, 1, 168);
        Range(errors, "Max catch up (days)", d.MaxCatchUpDays, 1, 30);
        Range(errors, "History retention (days)", d.HistoryRetentionDays, 1, 3650);
        Range(errors, "Snippet length", d.SnippetChars, 50, 2000);
        Range(errors, "Max mails per folder", d.MaxMailsPerFolder, 10, 1000);

        if (string.IsNullOrWhiteSpace(d.Language) || d.Language.Length > 40)
        {
            errors.Add("Language must be set and at most 40 characters.");
        }

        if (d.UserContext.Length > 4000)
        {
            errors.Add("User context is limited to 4000 characters.");
        }

        if (d.PriorityKeywords.Length > 100 || d.PriorityKeywords.Any(k => k.Length > 60))
        {
            errors.Add("Use at most 100 keywords of at most 60 characters.");
        }

        if (!Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var ollamaUrl) || ollamaUrl.Scheme is not ("http" or "https"))
        {
            errors.Add("The Ollama URL must be an http or https address.");
        }

        if (string.IsNullOrWhiteSpace(o.Model) || o.Model.Length > 100)
        {
            errors.Add("Model must be set.");
        }

        Range(errors, "Context size", o.NumCtx, 2048, 262144);
        Range(errors, "Timeout (minutes)", o.TimeoutMinutes, 1, 240);

        if (o.Temperature is < 0 or > 2)
        {
            errors.Add("Temperature must be between 0 and 2.");
        }

        // Only Discord's own webhook host is accepted, because digests contain mail summaries.
        if (!string.IsNullOrWhiteSpace(dto.Discord.WebhookUrl)
            && !(Uri.TryCreate(dto.Discord.WebhookUrl.Trim(), UriKind.Absolute, out var hook)
                 && hook.Scheme == "https"
                 && hook.Host is "discord.com" or "discordapp.com"
                 && hook.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal)))
        {
            errors.Add("The webhook must be a https://discord.com/api/webhooks/... address.");
        }

        if (dto.Discord.PingRoleId.Trim() is { Length: > 0 } role && !role.All(char.IsAsciiDigit))
        {
            errors.Add("The role ID must be digits only.");
        }

        if (!string.IsNullOrWhiteSpace(dto.Graph.ClientId) && !Guid.TryParse(dto.Graph.ClientId, out _))
        {
            errors.Add("The Azure client ID must be a GUID.");
        }

        if (!dto.Graph.Authority.StartsWith("https://login.microsoftonline.com/", StringComparison.Ordinal))
        {
            errors.Add("The authority must start with https://login.microsoftonline.com/.");
        }

        return errors;
    }

    private static void Range(List<string> errors, string name, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add($"{name} must be between {min} and {max}.");
        }
    }
}
