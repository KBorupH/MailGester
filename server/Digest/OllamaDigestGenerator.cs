using System.Net.Http.Json;
using System.Text.Json;
using MailGester.Data;

namespace MailGester.Digest;

public sealed class OllamaDigestGenerator(IHttpClientFactory httpFactory, SettingsService settings)
{
    private static readonly string[] Categories = ["recruiter", "deadline", "finance", "security", "personal", "other"];
    private static readonly string[] Actions = ["reply", "read", "verify", "none"];

    private static readonly JsonSerializerOptions ResponseJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Constrains the output so the model cannot invent fields or free text where the code expects an enum.</summary>
    private static readonly object Schema = new
    {
        type = "object",
        properties = new
        {
            summary = new { type = "string" },
            notify = new { type = "boolean" },
            urgency = new { type = "string", @enum = new[] { "none", "low", "high" } },
            notifyReason = new { type = "string" },
            noteworthy = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "integer" },
                        category = new { type = "string", @enum = Categories },
                        why = new { type = "string" },
                        action = new { type = "string", @enum = Actions },
                        suspectedPhishing = new { type = "boolean" }
                    },
                    required = new[] { "id", "category", "why", "action", "suspectedPhishing" }
                }
            }
        },
        required = new[] { "summary", "notify", "urgency", "notifyReason", "noteworthy" }
    };

    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly SettingsService _settings = settings;

    public string Model => _settings.Current.Ollama.Model;

    public async Task<DigestResult> GenerateAsync(string system, string user, CancellationToken ct)
    {
        var o = _settings.Current.Ollama;

        var request = new
        {
            model = o.Model,
            stream = false,
            format = Schema,
            keep_alive = "2m",
            options = new { num_ctx = o.NumCtx, temperature = o.Temperature },
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };

        // A long local generation is normal, so the timeout comes from the settings instead of the HttpClient default.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(o.TimeoutMinutes));

        var http = _httpFactory.CreateClient(OllamaAdmin.ClientName);
        using var response = await http.PostAsJsonAsync($"{o.BaseUrl.TrimEnd('/')}/api/chat", request, timeout.Token);
        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        var content = json.RootElement.GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("Ollama returned an empty message.");

        var digest = JsonSerializer.Deserialize<DigestResult>(content, ResponseJson)
            ?? throw new InvalidOperationException("Ollama returned JSON that could not be read as a digest.");

        return digest with
        {
            Summary = digest.Summary ?? "",
            Urgency = digest.Urgency ?? "none",
            NotifyReason = digest.NotifyReason ?? "",
            Noteworthy = digest.Noteworthy ?? []
        };
    }
}
