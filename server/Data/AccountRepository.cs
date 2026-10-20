using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace MailGester.Data;

public sealed class AccountRepository(Db db, IDataProtectionProvider protection)
{
    private readonly Db _db = db;
    private readonly IDataProtector _protector = protection.CreateProtector("mailgester.accounts.v1");

    public async Task<List<MailAccount>> ListAsync(bool enabledOnly = false, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd(
            $"""
            SELECT id, kind, address, secret, enabled, cursor_utc
            FROM accounts
            {(enabledOnly ? "WHERE enabled = 1" : "")}
            ORDER BY id
            """);

        var accounts = new List<MailAccount>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            accounts.Add(Map(reader));
        }

        return accounts;
    }

    public async Task<MailAccount?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd(
            "SELECT id, kind, address, secret, enabled, cursor_utc FROM accounts WHERE id = $id", ("$id", id));

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<long> AddAsync(MailAccount account, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd(
            """
            INSERT INTO accounts (kind, address, secret, enabled)
            VALUES ($kind, $address, $secret, $enabled)
            RETURNING id
            """,
            ("$kind", account.Kind.ToString().ToLowerInvariant()),
            ("$address", account.Address),
            ("$secret", Protect(account.Secret)),
            ("$enabled", account.Enabled));

        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <param name="replaceSecret">False keeps the stored secret, so the UI never has to send it back.</param>
    public async Task UpdateAsync(MailAccount account, bool replaceSecret, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd(
            """
            UPDATE accounts
            SET address = $address,
                enabled = $enabled,
                secret = CASE WHEN $replace = 1 THEN $secret ELSE secret END
            WHERE id = $id
            """,
            ("$id", account.Id),
            ("$address", account.Address),
            ("$enabled", account.Enabled),
            ("$replace", replaceSecret ? 1 : 0),
            ("$secret", Protect(account.Secret)));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd("DELETE FROM accounts WHERE id = $id", ("$id", id));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SetCursorsAsync(IReadOnlyDictionary<long, DateTimeOffset> cursors, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        foreach (var (id, cursor) in cursors)
        {
            await using var command = connection.Cmd(
                "UPDATE accounts SET cursor_utc = $cursor WHERE id = $id", ("$cursor", cursor.Iso()), ("$id", id));
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private object? Protect(string? secret) =>
        string.IsNullOrEmpty(secret) ? null : _protector.Protect(Encoding.UTF8.GetBytes(secret));

    private MailAccount Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Kind = Enum.Parse<AccountKind>(reader.GetString(1), ignoreCase: true),
        Address = reader.GetString(2),
        Secret = reader.IsDBNull(3) ? null : Encoding.UTF8.GetString(_protector.Unprotect((byte[])reader[3])),
        Enabled = reader.GetBoolean(4),
        Cursor = reader.IsDBNull(5) ? null : SqliteExtensions.ParseIso(reader.GetString(5))
    };
}
