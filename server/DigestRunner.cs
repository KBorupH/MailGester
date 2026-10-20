using System.Globalization;
using MailGester.Data;
using MailGester.Digest;
using MailGester.Runtime;
using MailGester.Sources;

namespace MailGester;

public sealed class DigestRunner(
    MailSourceFactory sources,
    AccountRepository accounts,
    DigestRepository history,
    OllamaDigestGenerator ai,
    DiscordNotifier discord,
    SettingsService settings,
    KvStore kv,
    RunStatus status,
    ILogger<DigestRunner> log)
{
    private const string LastRunKey = "lastrun";

    /// <summary>Tokens kept free for the model's answer, on top of the prompt.</summary>
    private const int OutputReserve = 2500;

    private const int MaxKeywordItems = 25;

    private readonly MailSourceFactory _sources = sources;
    private readonly AccountRepository _accounts = accounts;
    private readonly DigestRepository _history = history;
    private readonly OllamaDigestGenerator _ai = ai;
    private readonly DiscordNotifier _discord = discord;
    private readonly SettingsService _settings = settings;
    private readonly KvStore _kv = kv;
    private readonly RunStatus _status = status;
    private readonly ILogger<DigestRunner> _log = log;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public bool IsBusy => _runLock.CurrentCount == 0;

    public async Task<DateTimeOffset?> GetLastRunAsync(CancellationToken ct) =>
        await _kv.GetTextAsync(LastRunKey, ct) is { } text ? SqliteExtensions.ParseIso(text) : null;

    /// <summary>A run needs at least one enabled mailbox and a webhook, otherwise there is nothing to read or nowhere to post.</summary>
    public async Task<bool> IsConfiguredAsync(CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(_settings.Current.Discord.WebhookUrl)
        && (await _accounts.ListAsync(enabledOnly: true, ct: ct)).Count > 0;

    /// <param name="aiFallback">
    /// When true a failing model does not throw. A degraded post with keyword matches is sent instead,
    /// so a broken model can never hide a mail completely.
    /// </param>
    public async Task RunAsync(bool aiFallback, CancellationToken ct)
    {
        await _runLock.WaitAsync(ct);
        _status.Begin();

        string? error = null;
        try
        {
            await RunCoreAsync(aiFallback, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            _status.Finish(error);
            _runLock.Release();
        }
    }

    private async Task RunCoreAsync(bool aiFallback, CancellationToken ct)
    {
        var d = _settings.Current.Digest;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(d.TimeZone);
        var accountList = await _accounts.ListAsync(enabledOnly: true, ct: ct);
        if (accountList.Count == 0)
        {
            throw new InvalidOperationException("There are no enabled mail accounts.");
        }

        var now = DateTimeOffset.UtcNow;
        var floor = now.AddDays(-d.MaxCatchUpDays);

        var mails = new List<MailItem>();
        var errors = new List<SourceError>();
        var newCursors = new Dictionary<long, DateTimeOffset>();
        var earliest = now;

        // Sequential on purpose: the MSAL cache is shared and volumes are small.
        foreach (var account in accountList)
        {
            _status.SetStage($"Reading {account.Address}");

            var since = account.Cursor ?? now.AddHours(-d.FirstRunLookbackHours);
            since = since < floor ? floor : since;
            earliest = since < earliest ? since : earliest;

            // The cursor is taken before the fetch so mail arriving mid fetch is picked up next run.
            var fetchStart = DateTimeOffset.UtcNow;
            try
            {
                mails.AddRange(await _sources.Create(account).FetchAsync(since, ct));
                newCursors[account.Id] = fetchStart;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Fetching {Account} failed", account.Address);
                errors.Add(new SourceError(account.Address, ex.Message));
            }
        }

        var byKeyword = mails.ToLookup(m => KeywordMatcher.Matches(m, d.PriorityKeywords));
        var hits = byKeyword[true].OrderByDescending(m => m.Received).ToList();
        var ordered = hits.Concat(byKeyword[false].OrderByDescending(m => m.Received)).ToList();

        DigestResult? result = null;
        string? failure = null;
        List<MailItem> shown = [];
        var omitted = 0;

        if (mails.Count > 0)
        {
            _status.SetStage("Asking the model");

            var system = DigestPrompt.System(d);
            var budget = _settings.Current.Ollama.NumCtx - DigestPrompt.EstimateTokens(system) - OutputReserve;
            (shown, omitted) = Fit(ordered, budget);

            try
            {
                var user = DigestPrompt.User(shown, omitted, accountList.Count, earliest, now, zone);
                result = await _ai.GenerateAsync(system, user, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && aiFallback)
            {
                _log.LogError(ex, "Digest generation failed, posting fallback");
                failure = ex.Message;
            }
        }

        var post = PostComposer.Compose(
            result, failure, shown, hits, errors, omitted, accountList.Count, d, _ai.Model);

        _status.SetStage("Posting to Discord");
        await _discord.PostAsync(post, ct);

        // State moves only after Discord accepted the post, so a failed post is retried with the same mail.
        await _accounts.SetCursorsAsync(newCursors, ct);
        await _history.InsertAsync(BuildRecord(result, failure, shown, hits, errors, omitted, accountList.Count, mails, post, earliest, now), ct);
        await _kv.SetTextAsync(LastRunKey, DateTimeOffset.UtcNow.Iso(), ct);
        await _history.PruneAsync(now.AddDays(-d.HistoryRetentionDays), ct);

        _log.LogInformation(
            "Digest posted: {Mails} mails, {Errors} fetch errors, ping={Ping}", mails.Count, errors.Count, post.Ping);
    }

    private DigestRecord BuildRecord(
        DigestResult? result,
        string? failure,
        IReadOnlyList<MailItem> shown,
        IReadOnlyList<MailItem> hits,
        List<SourceError> errors,
        int omitted,
        int mailboxCount,
        IReadOnlyList<MailItem> mails,
        DigestPost post,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var items = new List<DigestItemRecord>();

        if (result is not null)
        {
            foreach (var n in result.Noteworthy.Where(n => n.Id >= 1 && n.Id <= shown.Count).DistinctBy(n => n.Id))
            {
                var mail = shown[n.Id - 1];
                items.Add(ToItem("noteworthy", mail, n.Category, n.Why, n.Action, n.SuspectedPhishing));
            }
        }

        items.AddRange(hits.Take(MaxKeywordItems).Select(m => ToItem("keyword", m, null, null, null, false)));

        return new DigestRecord
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            WindowFromUtc = from,
            WindowToUtc = to,
            MailCount = mails.Count,
            JunkCount = mails.Count(m => m.IsJunk),
            Omitted = omitted,
            MailboxCount = mailboxCount,
            Pinged = post.Ping,
            PingReason = post.PingText,
            Urgency = result?.Urgency ?? "none",
            Summary = failure is not null
                ? $"The AI digest failed: {failure}"
                : result?.Summary ?? "No new mail since the last digest.",
            AiFailure = failure,
            Model = _ai.Model,
            FetchErrors = errors,
            Items = items
        };
    }

    private static DigestItemRecord ToItem(
        string kind, MailItem mail, string? category, string? why, string? action, bool phishing) => new()
    {
        Kind = kind,
        Account = mail.Account,
        Folder = mail.Folder,
        IsJunk = mail.IsJunk,
        Sender = mail.From,
        Subject = mail.Subject,
        ReceivedUtc = mail.Received,
        Category = category,
        Why = why,
        Action = action,
        Phishing = phishing
    };

    private static (List<MailItem> Shown, int Omitted) Fit(IReadOnlyList<MailItem> ordered, int budget)
    {
        var shown = new List<MailItem>();
        var used = 0;

        foreach (var mail in ordered)
        {
            used += DigestPrompt.EstimateTokens(mail);
            if (used > budget)
            {
                break;
            }

            shown.Add(mail);
        }

        return (shown, ordered.Count - shown.Count);
    }
}
