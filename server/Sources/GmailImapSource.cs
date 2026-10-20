using MailGester.Data;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace MailGester.Sources;

public sealed class GmailImapSource(MailAccount account, SettingsService settings) : IMailSource
{
    private const string Host = "imap.gmail.com";

    private readonly MailAccount _account = account;
    private readonly SettingsService _settings = settings;

    public long AccountId => _account.Id;
    public string Account => _account.Address;

    public async Task<IReadOnlyList<MailItem>> FetchAsync(DateTimeOffset since, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_account.Secret))
        {
            throw new InvalidOperationException("No app password is stored for this account.");
        }

        var options = _settings.Current.Digest;

        using var client = new ImapClient();
        await client.ConnectAsync(Host, 993, true, ct);
        await client.AuthenticateAsync(_account.Address, _account.Secret, ct);

        var items = new List<MailItem>();
        items.AddRange(await ReadFolderAsync(client.Inbox, "Inbox", false, since, options, ct));

        // Gmail exposes Spam as the SPECIAL-USE junk folder, so no hardcoded localized name is needed.
        items.AddRange(await ReadFolderAsync(client.GetFolder(SpecialFolder.Junk), "Spam", true, since, options, ct));

        await client.DisconnectAsync(true, ct);
        return items;
    }

    private async Task<List<MailItem>> ReadFolderAsync(
        IMailFolder folder, string label, bool isJunk, DateTimeOffset since, DigestOptions options, CancellationToken ct)
    {
        // ReadOnly keeps the fetch from marking anything as seen.
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        // IMAP SINCE only has day granularity, so the exact cutoff is applied on InternalDate below.
        var uids = await folder.SearchAsync(SearchQuery.DeliveredAfter(since.UtcDateTime.Date), ct);
        var summaries = await folder.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct);

        var recent = summaries
            .Where(s => s.InternalDate is { } d && d > since)
            .OrderByDescending(s => s.InternalDate)
            .Take(options.MaxMailsPerFolder)
            .ToList();

        var items = new List<MailItem>(recent.Count);
        foreach (var summary in recent)
        {
            var message = await folder.GetMessageAsync(summary.UniqueId, ct);
            items.Add(ToItem(message, label, isJunk, summary.InternalDate!.Value, options));
        }

        await folder.CloseAsync(false, ct);
        return items;
    }

    private MailItem ToItem(MimeMessage message, string folder, bool isJunk, DateTimeOffset received, DigestOptions options)
    {
        var body = message.TextBody is { Length: > 0 } text
            ? TextCleaner.Collapse(text)
            : TextCleaner.FromHtml(message.HtmlBody ?? "");

        return new MailItem(
            Account,
            folder,
            isJunk,
            message.From.ToString(),
            TextCleaner.Collapse(message.Subject ?? ""),
            received,
            TextCleaner.Truncate(body, options.SnippetChars));
    }
}
