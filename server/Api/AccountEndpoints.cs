using MailGester.Data;
using MailGester.Runtime;
using MailGester.Sources;
using Microsoft.Data.Sqlite;

namespace MailGester.Api;

public sealed record AccountView(
    long Id,
    string Kind,
    string Address,
    bool Enabled,
    bool HasSecret,
    bool SignedIn,
    DateTimeOffset? Cursor);

public sealed record AccountRequest(string? Kind, string? Address, string? AppPassword, bool Enabled);

public static class AccountEndpoints
{
    private const int SqliteConstraint = 19;

    public static void MapAccounts(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/accounts");

        group.MapGet("/", async (AccountRepository accounts, MsalTokenProvider tokens, CancellationToken ct) =>
        {
            var signedIn = await tokens.SignedInAddressesAsync();
            var list = await accounts.ListAsync(ct: ct);
            return Results.Ok(list.Select(a => ToView(a, signedIn.Contains(a.Address))));
        });

        group.MapPost("/", async (AccountRequest request, AccountRepository accounts, RunCoordinator coordinator, CancellationToken ct) =>
        {
            if (!Enum.TryParse<AccountKind>(request.Kind, ignoreCase: true, out var kind))
            {
                return Results.BadRequest(new { error = "Kind must be gmail or outlook." });
            }

            var address = request.Address?.Trim() ?? "";
            if (!IsAddress(address))
            {
                return Results.BadRequest(new { error = "Enter a valid email address." });
            }

            var secret = kind == AccountKind.Gmail ? Compact(request.AppPassword) : null;
            if (kind == AccountKind.Gmail && string.IsNullOrEmpty(secret))
            {
                return Results.BadRequest(new { error = "A Gmail account needs an app password." });
            }

            try
            {
                var id = await accounts.AddAsync(new MailAccount { Kind = kind, Address = address, Secret = secret, Enabled = request.Enabled }, ct);
                coordinator.WakeForSettings();
                return Results.Ok(ToView((await accounts.GetAsync(id, ct))!, false));
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraint)
            {
                return Results.Conflict(new { error = "That address is already added." });
            }
        });

        group.MapPut("/{id:long}", async (long id, AccountRequest request, AccountRepository accounts, MsalTokenProvider tokens, RunCoordinator coordinator, CancellationToken ct) =>
        {
            if (await accounts.GetAsync(id, ct) is not { } account)
            {
                return Results.NotFound();
            }

            var address = request.Address?.Trim() ?? account.Address;
            if (account.Kind == AccountKind.Outlook && !address.Equals(account.Address, StringComparison.OrdinalIgnoreCase))
            {
                // The sign in belongs to the address, so changing it would leave a token for the wrong mailbox.
                return Results.BadRequest(new { error = "Remove the Outlook account and add it again to change its address." });
            }

            if (!IsAddress(address))
            {
                return Results.BadRequest(new { error = "Enter a valid email address." });
            }

            var secret = Compact(request.AppPassword);
            account.Address = address;
            account.Enabled = request.Enabled;
            account.Secret = secret;

            try
            {
                await accounts.UpdateAsync(account, replaceSecret: account.Kind == AccountKind.Gmail && !string.IsNullOrEmpty(secret), ct);
                coordinator.WakeForSettings();

                var signedIn = (await tokens.SignedInAddressesAsync()).Contains(address);
                return Results.Ok(ToView((await accounts.GetAsync(id, ct))!, signedIn));
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraint)
            {
                return Results.Conflict(new { error = "That address is already added." });
            }
        });

        group.MapDelete("/{id:long}", async (long id, AccountRepository accounts, MsalTokenProvider tokens, CancellationToken ct) =>
        {
            if (await accounts.GetAsync(id, ct) is not { } account)
            {
                return Results.NotFound();
            }

            if (account.Kind == AccountKind.Outlook)
            {
                try
                {
                    await tokens.SignOutAsync(account.Address);
                }
                catch (InvalidOperationException)
                {
                    // No client ID set means there is no cached sign in to remove.
                }
            }

            await accounts.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:long}/signin", async (long id, AccountRepository accounts, OutlookSignInService signIn, SettingsService settings, CancellationToken ct) =>
        {
            if (await accounts.GetAsync(id, ct) is not { Kind: AccountKind.Outlook } account)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(settings.Current.Graph.ClientId))
            {
                return Results.BadRequest(new { error = "Set the Azure client ID under Settings first." });
            }

            return Results.Ok(signIn.Start(account));
        });

        group.MapGet("/{id:long}/signin", (long id, OutlookSignInService signIn) =>
            signIn.Get(id) is { } view ? Results.Ok(view) : Results.NotFound());

        group.MapPost("/{id:long}/test", async (long id, AccountRepository accounts, MailSourceFactory sources, CancellationToken ct) =>
        {
            if (await accounts.GetAsync(id, ct) is not { } account)
            {
                return Results.NotFound();
            }

            try
            {
                var mails = await sources.Create(account).FetchAsync(DateTimeOffset.UtcNow.AddHours(-1), ct);
                return Results.Ok(new { ok = true, mailCount = mails.Count, error = (string?)null });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new { ok = false, mailCount = 0, error = ex.Message });
            }
        });
    }

    private static AccountView ToView(MailAccount a, bool signedIn) => new(
        a.Id, a.Kind.ToString().ToLowerInvariant(), a.Address, a.Enabled, !string.IsNullOrEmpty(a.Secret), signedIn, a.Cursor);

    private static bool IsAddress(string value) =>
        value.Length is > 3 and <= 254 && value.Contains('@') && !value.Any(char.IsWhiteSpace);

    /// <summary>Google shows app passwords in groups separated by spaces, which are not part of the password.</summary>
    private static string? Compact(string? value) => value?.Replace(" ", "").Trim();
}
