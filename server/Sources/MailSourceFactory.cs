using MailGester.Data;

namespace MailGester.Sources;

public sealed class MailSourceFactory(
    SettingsService settings,
    MsalTokenProvider tokens,
    IHttpClientFactory httpFactory)
{
    private readonly SettingsService _settings = settings;
    private readonly MsalTokenProvider _tokens = tokens;
    private readonly IHttpClientFactory _httpFactory = httpFactory;

    public IMailSource Create(MailAccount account) => account.Kind switch
    {
        AccountKind.Gmail => new GmailImapSource(account, _settings),
        AccountKind.Outlook => new GraphMailSource(account, _httpFactory.CreateClient(), _tokens, _settings),
        _ => throw new ArgumentOutOfRangeException(nameof(account))
    };
}
