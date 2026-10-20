using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MailGester.Data;

namespace MailGester.Auth;

/// <summary>Single admin password, set on first visit. Stored as a salted PBKDF2 hash.</summary>
public sealed class AdminAuth(KvStore kv)
{
    private const string Key = "admin";
    private const int Iterations = 210_000;
    public const int MinLength = 10;

    private readonly KvStore _kv = kv;

    public async Task<bool> HasPasswordAsync(CancellationToken ct = default) =>
        await _kv.GetTextAsync(Key, ct) is not null;

    public async Task SetPasswordAsync(string password, CancellationToken ct = default)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        await _kv.SetTextAsync(Key, $"v1:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}", ct);
    }

    public async Task<bool> VerifyAsync(string password, CancellationToken ct = default)
    {
        var stored = await _kv.GetTextAsync(Key, ct);
        var parts = stored?.Split(':');
        if (parts is not { Length: 4 } || parts[0] != "v1")
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, int.Parse(parts[1]), HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>Blocks an address for a minute after five failed logins. In memory is enough for a single instance.</summary>
public sealed class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset Until)> _state = new();

    public bool IsBlocked(string client) =>
        _state.TryGetValue(client, out var s) && s.Until > DateTimeOffset.UtcNow;

    public void Failed(string client) =>
        _state.AddOrUpdate(
            client,
            _ => (1, DateTimeOffset.MinValue),
            (_, s) => s.Failures + 1 >= MaxFailures
                ? (0, DateTimeOffset.UtcNow + Lockout)
                : (s.Failures + 1, s.Until));

    public void Succeeded(string client) => _state.TryRemove(client, out _);
}
