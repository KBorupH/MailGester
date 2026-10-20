using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MailGester.Data;

public sealed class SettingsService(KvStore kv, IDataProtectionProvider protection)
{
    private const string Key = "settings";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly KvStore _kv = kv;
    private readonly IDataProtector _protector = protection.CreateProtector("mailgester.settings.v1");
    private volatile AppSettings _current = new();

    /// <summary>Immutable by convention: callers read it, and changes go through <see cref="SaveAsync"/>.</summary>
    public AppSettings Current => _current;

    /// <summary>Raised after a save so the scheduler can recompute its next run.</summary>
    public event Action? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (await _kv.GetAsync(Key, ct) is not { } raw)
        {
            return;
        }

        try
        {
            _current = JsonSerializer.Deserialize<AppSettings>(_protector.Unprotect(raw), Json) ?? new AppSettings();
        }
        catch (CryptographicException ex)
        {
            // Failing loudly beats silently starting with defaults and overwriting the real settings on the next save.
            throw new InvalidOperationException(
                "Stored settings cannot be decrypted. The Data Protection key ring in the data volume is missing or has changed.", ex);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        var protectedBytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(settings, Json));
        await _kv.SetAsync(Key, protectedBytes, ct);

        _current = settings;
        Changed?.Invoke();
    }
}
