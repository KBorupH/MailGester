using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MailGester.Data;

namespace MailGester.Sources;

public sealed class GraphMailSource(
    MailAccount account,
    HttpClient http,
    MsalTokenProvider tokens,
    SettingsService settings) : IMailSource
{
    private const int PageSize = 50;

    private readonly MailAccount _account = account;
    private readonly HttpClient _http = http;
    private readonly MsalTokenProvider _tokens = tokens;
    private readonly SettingsService _settings = settings;

    public long AccountId => _account.Id;
    public string Account => _account.Address;

    public async Task<IReadOnlyList<MailItem>> FetchAsync(DateTimeOffset since, CancellationToken ct)
    {
        var token = await _tokens.AcquireTokenAsync(Account, ct);
        var options = _settings.Current.Digest;

        var items = new List<MailItem>();
        items.AddRange(await ReadFolderAsync("inbox", "Inbox", false, since, token, options, ct));
        items.AddRange(await ReadFolderAsync("junkemail", "Junk", true, since, token, options, ct));
        return items;
    }

    private async Task<List<MailItem>> ReadFolderAsync(
        string folderId, string label, bool isJunk, DateTimeOffset since, string token, DigestOptions options, CancellationToken ct)
    {
        // Invariant culture and quoted colons: a Danish culture would turn the time separator into a dot.
        var cutoff = since.UtcDateTime.ToString("yyyy-MM-ddTHH':'mm':'ssZ", CultureInfo.InvariantCulture);
        var filter = $"receivedDateTime gt {cutoff}";
        string? url = $"https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages"
            + $"?$filter={Uri.EscapeDataString(filter)}&$orderby=receivedDateTime desc"
            + $"&$select=subject,from,receivedDateTime,bodyPreview&$top={PageSize}";

        var items = new List<MailItem>();

        while (url is not null && items.Count < options.MaxMailsPerFolder)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            foreach (var message in json.RootElement.GetProperty("value").EnumerateArray())
            {
                items.Add(ToItem(message, label, isJunk, options));
            }

            url = json.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return items.Take(options.MaxMailsPerFolder).ToList();
    }

    private MailItem ToItem(JsonElement message, string folder, bool isJunk, DigestOptions options)
    {
        var from = message.TryGetProperty("from", out var f) && f.TryGetProperty("emailAddress", out var e)
            ? $"{Text(e, "name")} <{Text(e, "address")}>"
            : "unknown";

        return new MailItem(
            Account,
            folder,
            isJunk,
            from,
            TextCleaner.Collapse(Text(message, "subject")),
            message.GetProperty("receivedDateTime").GetDateTimeOffset(),
            TextCleaner.Truncate(TextCleaner.Collapse(Text(message, "bodyPreview")), options.SnippetChars));
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
