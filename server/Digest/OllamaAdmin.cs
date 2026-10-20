using System.Net.Http.Json;
using System.Text.Json;
using MailGester.Data;

namespace MailGester.Digest;

public sealed record OllamaCheck(bool Reachable, string? Error, string[] Models, bool ModelInstalled);

public sealed record PullView(bool Running, string Status, int Percent, string? Error);

/// <summary>Connectivity check and model pull, so Ollama can be managed without a terminal.</summary>
public sealed class OllamaAdmin(IHttpClientFactory httpFactory, SettingsService settings)
{
    public const string ClientName = "ollama";

    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly SettingsService _settings = settings;
    private readonly object _gate = new();
    private PullView _pull = new(false, "", 0, null);

    public async Task<OllamaCheck> CheckAsync(CancellationToken ct)
    {
        var o = _settings.Current.Ollama;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            using var response = await _httpFactory.CreateClient(ClientName)
                .GetAsync($"{o.BaseUrl.TrimEnd('/')}/api/tags", timeout.Token);
            response.EnsureSuccessStatusCode();

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var models = json.RootElement.GetProperty("models").EnumerateArray()
                .Select(m => m.GetProperty("name").GetString() ?? "")
                .ToArray();

            // Ollama lists "name:tag", and a bare name means ":latest".
            var wanted = o.Model.Contains(':') ? o.Model : o.Model + ":latest";
            return new OllamaCheck(true, null, models, models.Contains(wanted, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new OllamaCheck(false, ex is OperationCanceledException ? "Timed out." : ex.Message, [], false);
        }
    }

    public PullView Pull
    {
        get
        {
            lock (_gate)
            {
                return _pull;
            }
        }
    }

    /// <summary>Starts a background pull of the configured model. Returns false if one is already running.</summary>
    public bool StartPull()
    {
        var o = _settings.Current.Ollama;

        lock (_gate)
        {
            if (_pull.Running)
            {
                return false;
            }

            _pull = new PullView(true, "starting", 0, null);
        }

        _ = Task.Run(() => PullAsync(o.BaseUrl, o.Model));
        return true;
    }

    private async Task PullAsync(string baseUrl, string model)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/pull")
            {
                Content = JsonContent.Create(new { model, stream = true })
            };

            using var response = await _httpFactory.CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            while (await reader.ReadLineAsync() is { } line)
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;

                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException(error.GetString());
                }

                var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                var percent = root.TryGetProperty("total", out var total) && root.TryGetProperty("completed", out var done) && total.GetInt64() > 0
                    ? (int)(done.GetInt64() * 100 / total.GetInt64())
                    : 0;

                Set(new PullView(true, status, percent, null));
            }

            Set(new PullView(false, "success", 100, null));
        }
        catch (Exception ex)
        {
            Set(new PullView(false, "failed", 0, ex.Message));
        }
    }

    private void Set(PullView view)
    {
        lock (_gate)
        {
            _pull = view;
        }
    }
}
