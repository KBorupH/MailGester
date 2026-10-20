using MailGester.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Identity.Client;
using System.Security.Cryptography;

namespace MailGester.Sources;

/// <summary>
/// One MSAL public client with an encrypted, file backed token cache shared by all Outlook accounts.
/// The client is rebuilt when the client id or authority changes in the settings.
/// </summary>
public sealed class MsalTokenProvider(SettingsService settings, DataPaths paths, IDataProtectionProvider protection)
{
    private static readonly string[] Scopes = ["Mail.Read"];

    private readonly SettingsService _settings = settings;
    private readonly DataPaths _paths = paths;
    private readonly IDataProtector _protector = protection.CreateProtector("mailgester.msal.v1");
    private readonly object _gate = new();
    private IPublicClientApplication? _app;
    private string _appKey = "";

    private IPublicClientApplication App()
    {
        var graph = _settings.Current.Graph;
        if (string.IsNullOrWhiteSpace(graph.ClientId))
        {
            throw new InvalidOperationException("The Azure client ID is not set. Add it under Settings, Outlook.");
        }

        lock (_gate)
        {
            var key = $"{graph.ClientId}|{graph.Authority}";
            if (_app is null || _appKey != key)
            {
                _app = Build(graph);
                _appKey = key;
            }

            return _app;
        }
    }

    private IPublicClientApplication Build(GraphOptions graph)
    {
        var app = PublicClientApplicationBuilder
            .Create(graph.ClientId)
            .WithAuthority(graph.Authority)
            .Build();

        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (!File.Exists(_paths.MsalCache))
            {
                return;
            }

            try
            {
                args.TokenCache.DeserializeMsalV3(_protector.Unprotect(File.ReadAllBytes(_paths.MsalCache)));
            }
            catch (CryptographicException)
            {
                // An unreadable cache just means the accounts have to sign in again.
            }
        });

        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged)
            {
                return;
            }

            Directory.CreateDirectory(_paths.Root);
            File.WriteAllBytes(_paths.MsalCache, _protector.Protect(args.TokenCache.SerializeMsalV3()));
        });

        return app;
    }

    public async Task<string> AcquireTokenAsync(string address, CancellationToken ct)
    {
        var app = App();
        var account = (await app.GetAccountsAsync())
            .FirstOrDefault(a => a.Username.Equals(address, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{address} is not signed in. Use Sign in on the Accounts page.");

        try
        {
            return (await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct)).AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            throw new InvalidOperationException($"The sign in for {address} has expired. Sign in again on the Accounts page.");
        }
    }

    public async Task<HashSet<string>> SignedInAddressesAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.Current.Graph.ClientId))
        {
            return [];
        }

        var accounts = await App().GetAccountsAsync();
        return accounts.Select(a => a.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public Task<AuthenticationResult> LoginAsync(Func<DeviceCodeResult, Task> onCode, CancellationToken ct) =>
        App().AcquireTokenWithDeviceCode(Scopes, onCode).ExecuteAsync(ct);

    public async Task SignOutAsync(string address)
    {
        var app = App();
        var account = (await app.GetAccountsAsync())
            .FirstOrDefault(a => a.Username.Equals(address, StringComparison.OrdinalIgnoreCase));

        if (account is not null)
        {
            await app.RemoveAsync(account);
        }
    }
}
