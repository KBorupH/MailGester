using System.Collections.Concurrent;

namespace MailGester.Sources;

public sealed record SignInView(string Status, string? UserCode, string? VerificationUrl, string? Error);

/// <summary>Runs the device code flow in the background so the UI can show the code and poll for the result.</summary>
public sealed class OutlookSignInService(MsalTokenProvider tokens)
{
    private sealed class Flow
    {
        public required CancellationTokenSource Cts { get; init; }
        public volatile string Status = "pending";
        public volatile string? UserCode;
        public volatile string? VerificationUrl;
        public volatile string? Error;
    }

    private readonly MsalTokenProvider _tokens = tokens;
    private readonly ConcurrentDictionary<long, Flow> _flows = new();

    public SignInView Start(MailAccount account)
    {
        // A second click replaces the earlier attempt instead of leaving two codes alive.
        if (_flows.TryRemove(account.Id, out var previous))
        {
            previous.Cts.Cancel();
        }

        var flow = new Flow { Cts = new CancellationTokenSource(TimeSpan.FromMinutes(16)) };
        _flows[account.Id] = flow;

        _ = Task.Run(() => RunAsync(account, flow));
        return Snapshot(flow);
    }

    public SignInView? Get(long accountId) =>
        _flows.TryGetValue(accountId, out var flow) ? Snapshot(flow) : null;

    private async Task RunAsync(MailAccount account, Flow flow)
    {
        try
        {
            var result = await _tokens.LoginAsync(code =>
            {
                flow.UserCode = code.UserCode;
                flow.VerificationUrl = code.VerificationUrl;
                return Task.CompletedTask;
            }, flow.Cts.Token);

            if (!result.Account.Username.Equals(account.Address, StringComparison.OrdinalIgnoreCase))
            {
                // Signed in with a different mailbox than the one configured: drop it so it cannot be used by mistake.
                await _tokens.SignOutAsync(result.Account.Username);
                flow.Error = $"You signed in as {result.Account.Username}, but this account is {account.Address}.";
                flow.Status = "failed";
                return;
            }

            flow.Status = "done";
        }
        catch (Exception ex)
        {
            flow.Error = ex is OperationCanceledException ? "The code expired or the sign in was cancelled." : ex.Message;
            flow.Status = "failed";
        }
    }

    private static SignInView Snapshot(Flow flow) => new(flow.Status, flow.UserCode, flow.VerificationUrl, flow.Error);
}
