using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailGester.Sources;
using MailGester.Data;

namespace MailGester.Digest;

public sealed record PostField(string Name, string Value);

public sealed record DigestPost(
    bool Ping,
    string PingText,
    string Title,
    string Description,
    IReadOnlyList<PostField> Fields,
    string Footer);

public sealed class DiscordNotifier(IHttpClientFactory httpFactory, SettingsService settings)
{
    // Embed limits from the Discord API: description 4096, field value 1024, 6000 characters in total.
    private const int DescriptionMax = 4000;
    private const int FieldValueMax = 1000;
    private const int TotalMax = 5500;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly SettingsService _settings = settings;

    /// <summary>Posts a visible test message that also pings the role, so a wrong role ID shows up immediately.</summary>
    public Task SendTestAsync(CancellationToken ct) =>
        PostAsync(new DigestPost(
            true,
            "This is a test ping from MailGester. You can ignore it.",
            "MailGester test",
            "If you can read this, the webhook works. If the role was mentioned above, the role ID works too.",
            [],
            "test"), ct);

    public async Task PostAsync(DigestPost post, CancellationToken ct)
    {
        var discord = _settings.Current.Discord;
        if (string.IsNullOrWhiteSpace(discord.WebhookUrl))
        {
            throw new InvalidOperationException("No Discord webhook is configured.");
        }

        var role = discord.PingRoleId;
        var mention = post.Ping && role.Length > 0 ? $"<@&{role}> " : "";

        var description = TextCleaner.Truncate(post.Description, DescriptionMax);
        var total = post.Title.Length + description.Length + post.Footer.Length;
        var fields = new List<object>();

        foreach (var field in post.Fields)
        {
            var value = TextCleaner.Truncate(field.Value, FieldValueMax);
            if (fields.Count >= 25 || total + field.Name.Length + value.Length > TotalMax)
            {
                break;
            }

            total += field.Name.Length + value.Length;
            fields.Add(new { name = TextCleaner.Truncate(field.Name, 250), value, inline = false });
        }

        var payload = new
        {
            username = "MailGester",
            content = post.Ping ? mention + post.PingText : null,
            // Only the configured role may be mentioned, so text copied from a mail cannot ping anyone.
            allowed_mentions = new { roles = post.Ping && role.Length > 0 ? new[] { role } : Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = post.Title,
                    description,
                    color = post.Ping ? 0xE5533D : 0x3B6FE0,
                    fields,
                    footer = new { text = post.Footer },
                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                }
            }
        };

        var http = _httpFactory.CreateClient();

        for (var attempt = 0; ; attempt++)
        {
            using var response = await http.PostAsJsonAsync(discord.WebhookUrl, payload, Json, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                await Task.Delay(wait, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return;
        }
    }
}
