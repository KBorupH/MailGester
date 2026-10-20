namespace MailGester.Sources;

public interface IMailSource
{
    long AccountId { get; }

    /// <summary>Mailbox address, used as the label on every mail.</summary>
    string Account { get; }

    /// <summary>Returns mail received after <paramref name="since"/> from the inbox and the junk folder.</summary>
    Task<IReadOnlyList<MailItem>> FetchAsync(DateTimeOffset since, CancellationToken ct);
}
